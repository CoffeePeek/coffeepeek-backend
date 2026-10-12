<#
Run against an isolated local stack with a visible shop and three distinct test accounts.
Author and Other must have no check-in at the selected shop in the last three hours and fewer than six check-ins today.
Tokens are read from CHECKIN_AUTHOR_TOKEN, CHECKIN_OTHER_TOKEN and CHECKIN_MODERATOR_TOKEN.
Optional PhotoJson is a fresh check-in UploadedPhotoDto uploaded by the OtherToken account.
PreviousPublicCheckInId can select that account's older approved visit to test same-author rating fallback.
Only check-ins created by this run are deleted. No requests are sent without -Run.
#>
[CmdletBinding()]
param(
    [switch]$SelfTest,
    [switch]$Run,
    [uri]$BaseUrl = 'http://localhost:8080',
    [string]$CoffeeShopSlug,
    [string]$AuthorToken = $env:CHECKIN_AUTHOR_TOKEN,
    [string]$OtherToken = $env:CHECKIN_OTHER_TOKEN,
    [string]$ModeratorToken = $env:CHECKIN_MODERATOR_TOKEN,
    [string]$PhotoJson,
    [guid]$PreviousPublicCheckInId,
    [ValidateRange(5, 120)][int]$TimeoutSeconds = 30,
    [switch]$KeepCreated
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-LocalTarget([uri]$Target) {
    Assert ($Target.IsAbsoluteUri -and $Target.Scheme -in @('http', 'https') -and
        $Target.IsLoopback -and -not $Target.UserInfo -and -not $Target.Query -and
        -not $Target.Fragment -and $Target.AbsolutePath -eq '/') 'Only an isolated loopback HTTP base URL is accepted.'
}
function New-Body([string]$Text, [int]$Score) {
    @{ coffeeShopSlug = $CoffeeShopSlug; visitedAt = [datetimeoffset]::UtcNow.ToString('o'); text = $Text; rating = @{ coffee = $Score; service = $Score; place = $Score } }
}
function Request([string]$Method, [string]$Path, [string]$Token = '', $Body = $null) {
    $requestArgs = @{ Uri = [uri]::new($BaseUrl, $Path); Method = $Method; SkipHttpErrorCheck = $true;
        TimeoutSec = $TimeoutSeconds; MaximumRedirection = 0; Headers = @{} }
    if ($Token) { $requestArgs.Headers.Authorization = "Bearer $Token" }
    if ($null -ne $Body) { $requestArgs.ContentType = 'application/json'; $requestArgs.Body = $Body | ConvertTo-Json -Depth 10 -Compress }
    $response = Invoke-WebRequest @requestArgs
    $json = $null
    if ($response.Headers['Content-Type'] -match 'json' -and $response.Content) {
        $json = $response.Content | ConvertFrom-Json
    }
    @{ Status = [int]$response.StatusCode; Json = $json; Headers = $response.Headers; Bytes = $response.RawContentLength }
}
function Get-Data($Response) {
    Assert ($Response.Status -ge 200 -and $Response.Status -lt 300) "Unexpected HTTP status $($Response.Status)."
    Assert ($null -ne $Response.Json -and $Response.Json.isSuccess) 'API returned an unsuccessful result.'
    $Response.Json.data
}
function Wait-Until([scriptblock]$Probe, [string]$Message) {
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (& $Probe) { return }
        Start-Sleep -Milliseconds 500
    } while ([datetime]::UtcNow -lt $deadline)
    throw $Message
}
function Find-Submission([guid]$CheckInId, [int]$Revision) {
    $found = $null
    Wait-Until {
        for ($page = 1; $page -le 100; $page++) {
            $result = Get-Data (Request GET "/api/v1/moderation/check-ins?page=$page&pageSize=100" $ModeratorToken)
            $matches = @($result.items | Where-Object { $_.checkInId -eq $CheckInId -and $_.contentRevision -eq $Revision })
            Assert ($matches.Count -le 1) 'Ambiguous smoke submission identity.'
            if ($matches.Count) { $script:smokeSubmission = $matches[0]; return $true }
            if ($page -ge $result.totalPages) { break }
        }
        $false
    } 'Moderation snapshot did not arrive.'
    $found = $script:smokeSubmission
    $script:smokeSubmission = $null
    $found
}
function Decide([guid]$SubmissionId, [string]$Status) {
    $response = Request PUT '/api/v1/moderation/check-ins' $ModeratorToken @{
        submissionId = $SubmissionId; moderationStatus = $Status
        comment = $null; rejectReason = $null
    }
    Assert ($response.Status -eq 200 -and $response.Json.isSuccess) 'Moderation status update failed.'
}
function Feed {
    $items = [System.Collections.Generic.List[object]]::new()
    $cursor = ''
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    do {
        $path = "/api/v1/check-ins?coffeeShopSlug=$CoffeeShopSlug&pageSize=100"
        if ($cursor) { $path += '&cursor=' + [uri]::EscapeDataString($cursor) }
        $page = Get-Data (Request GET $path)
        foreach ($item in $page.items) {
            Assert ($item.shop.slug -eq $CoffeeShopSlug -and $seen.Add([string]$item.id)) 'Feed filter or cursor returned a duplicate.'
            $items.Add($item)
        }
        $cursor = $page.nextCursor
        Assert ($items.Count -le 10000) 'Use a smaller isolated fixture: smoke feed exceeds 10,000 entries.'
    } while ($cursor)
    $items.ToArray()
}
function Assert-Rating([guid]$ExpectedLatestId = [guid]::Empty) {
    $all = @(Feed)
    $selected = @($all | Where-Object { $null -ne $_.rating } | Group-Object userId | ForEach-Object {
        $_.Group | Sort-Object @{ Expression = { [datetime]$_.visitedAt }; Descending = $true },
            @{ Expression = { [datetime]$_.createdAtUtc }; Descending = $true },
            @{ Expression = { [string]$_.id }; Descending = $true } | Select-Object -First 1
    })
    if ($ExpectedLatestId -ne [guid]::Empty) {
        Assert (@($selected | Where-Object { $_.id -eq $ExpectedLatestId }).Count -eq 1) 'Expected latest eligible score was not selected; check the isolated history fixture.'
    }
    [decimal]$expected = 0
    if ($selected.Count) {
        $sum = [decimal]0
        foreach ($item in $selected) { $sum += ($item.rating.coffee + $item.rating.service + $item.rating.place) / [decimal]3 }
        $expected = $sum / $selected.Count
    }
    $shop = (Get-Data (Request GET "/api/CoffeeShops/$CoffeeShopSlug")).shopDto
    Assert ([math]::Abs([decimal]$shop.rating - $expected) -le 0.011) 'Shop rating disagrees with the latest eligible score per author.'
    Assert ($shop.checkInCount -eq $all.Count) 'Shop check-in count disagrees with all eligible visits.'
}

function Assert-RetiredRoutes([guid]$CheckInId, [guid]$AuthorId) {
    foreach ($path in @('/api/v2/check-ins', '/api/CheckIns', '/api/CoffeeShopReviews',
            "/api/users/$AuthorId/reviews", "/api/CoffeeShopReviews/$CheckInId", '/api/ModerationReviews')) {
        Assert ((Request GET $path $ModeratorToken).Status -eq 404) "Retired route remains available: $path"
    }
    Assert ((Request POST '/api/CheckIns' $AuthorToken (New-Body 'retired route probe' 3)).Status -eq 404) 'Retired check-in creation route remains available.'
    Assert ((Request POST '/api/Photos/review' $AuthorToken @()).Status -eq 404) 'Retired review upload route remains available.'
}

if ($SelfTest) {
    Assert-LocalTarget ([uri]'http://127.0.0.1:8080/')
    foreach ($url in @('https://example.com', 'http://localhost.evil.test', 'file:///tmp/x', 'http://user@localhost', 'http://localhost/api', 'http://localhost/?x=1')) {
        $rejected = $false
        try { Assert-LocalTarget ([uri]$url) } catch { $rejected = $true }
        Assert $rejected "Unsafe target accepted: $url"
    }
    $body = New-Body 'text' 3
    Assert ($body.Count -eq 4 -and $body.ContainsKey('visitedAt') -and $body.rating.Count -eq 3 -and -not $body.ContainsKey('photos')) 'Creation body is missing its required visit date.'
    Assert ('Private' -in @(0, 'Private') -and 0 -in @(0, 'Private')) 'Both named and numeric enum responses must be supported.'
    $failed = $false
    try { Get-Data @{ Status = 200; Json = @{ isSuccess = $false } } } catch { $failed = $true }
    Assert $failed 'An unsuccessful 200 response was accepted.'
    Assert ((Get-Command Find-Submission).ScriptBlock.ToString() -match '/api/v1/moderation/check-ins' -and
        (Get-Command Decide).ScriptBlock.ToString() -match 'submissionId') 'Moderator route or submission body is stale.'
    Assert ((Get-Command Feed).ScriptBlock.ToString() -match '/api/v1/check-ins') 'Public feed route is stale.'
    Write-Output 'PASS: smoke target guards, minimal payload, unsuccessful-response assertion and canonical paths. No HTTP requests sent.'
    return
}
if (-not $Run) {
    Write-Output 'DRY RUN: no HTTP requests. Use -SelfTest, or -Run against an isolated local stack with -CoffeeShopSlug and three tokens.'
    return
}
Assert-LocalTarget $BaseUrl
Assert (-not [string]::IsNullOrWhiteSpace($CoffeeShopSlug)) '-CoffeeShopSlug is required.'
Assert ($AuthorToken -and $OtherToken -and $ModeratorToken -and $AuthorToken -ne $OtherToken -and
    $AuthorToken -ne $ModeratorToken -and $OtherToken -ne $ModeratorToken) 'Three distinct test-account tokens are required.'
$created = [System.Collections.Generic.List[object]]::new()
$marker = 'checkin-smoke-' + [guid]::NewGuid().ToString('N')
try {
    Assert ((Request GET '/api/v1/moderation/check-ins' $AuthorToken).Status -eq 403) 'Standard user could read the moderator queue.'
    $invalid = New-Body $marker 3
    $invalid.Remove('visitedAt')
    Assert ((Request POST '/api/v1/check-ins' $AuthorToken $invalid).Status -eq 400) 'Missing visit date was accepted.'
    $invalid = New-Body $marker 3
    $invalid.rating.coffee = 0
    Assert ((Request POST '/api/v1/check-ins' $AuthorToken $invalid).Status -eq 400) 'Invalid score was accepted.'
    $first = Get-Data (Request POST '/api/v1/check-ins' $AuthorToken (New-Body "$marker first" 2))
    $created.Add(@{ Id = [guid]$first.id; Token = $AuthorToken })
    Assert-RetiredRoutes ([guid]$first.id) ([guid]$first.userId)
    Assert ($first.visibility -in @(0, 'Private') -and $first.photos.Count -eq 0 -and $null -eq $first.drink) 'Optional fields/default privacy failed.'
    Assert ([math]::Abs(([datetime]$first.visitedAt - [datetime]::UtcNow).TotalMinutes) -lt 2) 'Omitted visit time did not default to now.'
    Assert ((Request GET "/api/v1/check-ins/$($first.id)").Status -eq 404) 'Private check-in readable anonymously.'
    Assert ((Request GET "/api/v1/check-ins/$($first.id)" $OtherToken).Status -eq 404) 'Private check-in readable by another user.'
    $foreignUpdate = Request PUT "/api/v1/check-ins/$($first.id)" $OtherToken @{ text = 'foreign'; rating = $first.rating }
    Assert ($foreignUpdate.Status -in @(403, 404)) 'Another user could edit a private check-in.'
    $null = Get-Data (Request PUT "/api/v1/check-ins/$($first.id)/visibility" $AuthorToken @{ visibility = 1 })
    Assert ((Request GET "/api/v1/check-ins/$($first.id)").Status -eq 404) 'Pending check-in became public before approval.'
    $submission1 = Find-Submission ([guid]$first.id) ([int]$first.contentRevision)
    Decide $submission1.id Approved
    Wait-Until { (Request GET "/api/v1/check-ins/$($first.id)").Status -eq 200 } 'Approved check-in did not become public.'
    Decide $submission1.id Approved
    Assert-Rating

    $body2 = New-Body "$marker second" 5
    $body2.visibility = 1
    if ($PhotoJson) { $body2.photos = @((Get-Content -LiteralPath $PhotoJson -Raw | ConvertFrom-Json)) }
    # One creation per account keeps the real three-hour per-shop cooldown intact.
    $second = Get-Data (Request POST '/api/v1/check-ins' $OtherToken $body2)
    $created.Add(@{ Id = [guid]$second.id; Token = $OtherToken })
    if ($PreviousPublicCheckInId -ne [guid]::Empty) {
        $previous = Get-Data (Request GET "/api/v1/check-ins/$PreviousPublicCheckInId")
        Assert ($previous.author.slug -eq $second.author.slug -and $previous.shop.slug -eq $CoffeeShopSlug -and
            [datetime]$previous.visitedAt -lt [datetime]$second.visitedAt) 'PreviousPublicCheckInId must be an older approved visit of the second author in the same shop.'
    }
    $submission2 = Find-Submission ([guid]$second.id) ([int]$second.contentRevision)
    Decide $submission2.id Approved
    Wait-Until { (Request GET "/api/v1/check-ins/$($second.id)").Status -eq 200 } 'Second approval was not applied.'
    Assert-Rating ([guid]$second.id)
    $vote = Get-Data (Request PUT "/api/v1/check-ins/$($second.id)/helpful" $AuthorToken)
    Assert ($vote.isHelpful -and $vote.helpfulCount -ge 1) 'Canonical helpful vote failed.'
    $voteAgain = Get-Data (Request PUT "/api/v1/check-ins/$($second.id)/helpful" $AuthorToken)
    Assert ($voteAgain.helpfulCount -eq $vote.helpfulCount) 'Duplicate vote changed the count.'
    $byAuthor = Get-Data (Request GET "/api/v1/check-ins?coffeeShopSlug=$CoffeeShopSlug&authorSlug=$($first.author.slug)&pageSize=100")
    Assert (@($byAuthor.items | Where-Object { $_.userId -ne $first.userId }).Count -eq 0) 'Author feed filter returned another author.'
    $page1 = Get-Data (Request GET "/api/v1/check-ins?coffeeShopSlug=$CoffeeShopSlug&pageSize=1")
    Assert ($page1.items.Count -eq 1 -and $page1.nextCursor) 'Filtered feed did not produce a cursor.'
    $page2 = Get-Data (Request GET ("/api/v1/check-ins?coffeeShopSlug=$CoffeeShopSlug&pageSize=1&cursor=" + [uri]::EscapeDataString($page1.nextCursor)))
    Assert ($page2.items.Count -eq 1 -and $page1.items[0].id -ne $page2.items[0].id -and $page2.items[0].shop.slug -eq $CoffeeShopSlug) 'Filtered cursor is unstable.'
    Assert ((Request GET '/api/v1/check-ins?cursor=invalid!').Status -eq 400) 'Malformed cursor was accepted.'

    $photoPath = $null
    if ($PhotoJson) {
        $publicSecond = Get-Data (Request GET "/api/v1/check-ins/$($second.id)")
        Assert ($publicSecond.photos.Count -eq 1) 'Photo association was lost.'
        $photoPath = "/api/v1/check-ins/$($second.id)/photos/$($publicSecond.photos[0].id)"
        Assert ($publicSecond.photos[0].url -eq $photoPath) 'Public photo URL does not use the canonical route.'
        $moderationPhotoPath = "/api/v1/check-ins/$($second.id)/moderation-photos/$($publicSecond.photos[0].id)?revision=$($second.contentRevision)"
        Assert ($submission2.photos.Count -eq 1 -and $submission2.photos[0].fullUrl -eq $moderationPhotoPath) 'Snapshot photo URL does not use the protected canonical route.'
        Assert ((Request GET $moderationPhotoPath $AuthorToken).Status -eq 403) 'Standard user could read moderator photo bytes.'
        $moderatorBytes = Request GET $moderationPhotoPath $ModeratorToken
        Assert ($moderatorBytes.Status -eq 200 -and $moderatorBytes.Bytes -gt 0) 'Moderator could not read current submission photo bytes.'
        $bytes = Request GET $photoPath
        Assert ($bytes.Status -eq 200 -and $bytes.Bytes -gt 0) 'Approved photo bytes were not served.'
        Assert ($bytes.Headers['Cache-Control'] -match 'no-store' -and
            $bytes.Headers['X-Content-Type-Options'] -eq 'nosniff') 'Photo bytes are missing cache or MIME protections.'
    }
    $edited = Get-Data (Request PUT "/api/v1/check-ins/$($second.id)" $OtherToken @{
        text = "$marker edited"; rating = @{ coffee = 4; service = 4; place = 4 }
    })
    Assert ($edited.contentRevision -gt $second.contentRevision) 'Content revision did not advance.'
    Assert ((Request GET "/api/v1/check-ins/$($second.id)").Status -eq 404) 'Edited content retained public approval.'
    if ($photoPath) {
        Assert ((Request GET $photoPath).Status -eq 404) 'Edited photo bytes retained public approval.'
        Assert ((Request GET $moderationPhotoPath $ModeratorToken).Status -eq 404) 'Stale submission photo bytes remain available.'
    }
    Assert-Rating $PreviousPublicCheckInId
    Decide $submission2.id Pending
    Decide $submission2.id Approved
    Start-Sleep -Seconds 2
    Assert ((Request GET "/api/v1/check-ins/$($second.id)").Status -eq 404) 'Old submission approved a newer revision.'
    $submission3 = Find-Submission ([guid]$second.id) ([int]$edited.contentRevision)
    Decide $submission3.id Approved
    Wait-Until { (Request GET "/api/v1/check-ins/$($second.id)").Status -eq 200 } 'Edited revision approval was not applied.'
    $afterEdit = Get-Data (Request GET "/api/v1/check-ins/$($second.id)" $AuthorToken)
    Assert ($afterEdit.helpfulCount -eq $vote.helpfulCount -and $afterEdit.isHelpfulByCurrentUser) 'Vote history did not survive editing.'
    $null = Get-Data (Request PUT "/api/v1/check-ins/$($second.id)/visibility" $OtherToken @{ visibility = 0 })
    Assert ((Request GET "/api/v1/check-ins/$($second.id)").Status -eq 404) 'Hidden check-in is still public.'
    if ($photoPath) {
        Assert ((Request GET $photoPath).Status -in @(401, 403, 404)) 'Hidden photo bytes are public.'
        Assert ((Request GET $photoPath $AuthorToken).Status -in @(403, 404)) 'Hidden photo bytes readable by another user.'
        Assert ((Request GET $photoPath $OtherToken).Status -eq 200) 'Author cannot read hidden photo bytes.'
    }
    Assert-Rating $PreviousPublicCheckInId
    Write-Output 'PASS: retired routes, creation, privacy, approval, revisions, stale decisions, feed, rating recalculation and helpful history.'
    if (-not $PhotoJson) { Write-Output 'NOT RUN: photo byte checks; provide -PhotoJson with a fresh check-in upload to include them.' }
    if ($PreviousPublicCheckInId -eq [guid]::Empty) { Write-Output 'NOT RUN: asserted fallback to an earlier same-author score; supply -PreviousPublicCheckInId. Removal and aggregate recalculation are checked.' }
} finally {
    if (-not $KeepCreated) {
        foreach ($entry in $created) {
            try { $response = Request DELETE "/api/v1/check-ins/$($entry.Id)" $entry.Token; Assert ($response.Status -in @(200, 204, 404)) 'Cleanup failed.' }
            catch { Write-Warning "Could not delete smoke check-in $($entry.Id). Delete it with the author account." }
        }
    } else { Write-Output "Retained test check-ins: $(($created | ForEach-Object { $_.Id }) -join ', ')" }
}
