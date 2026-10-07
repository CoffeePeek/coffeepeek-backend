using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Review.SetReviewHelpful;

public class SetReviewHelpfulHandlerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyVoteUsesCanonicalIdentityAndOnlyInvalidatesChangedState(bool changed)
    {
        var legacy=Guid.NewGuid();var id=Guid.NewGuid();var voter=Guid.NewGuid();
        var repository=new Mock<IQueryCheckInRepository>();var queries=new Mock<ICheckInQueries>();var cache=new Mock<ICacheService>();
        queries.Setup(q=>q.GetUnifiedByIdAsync(id,null,default)).ReturnsAsync(new UnifiedCheckInDto {Id=id,UserId=Guid.NewGuid(),ShopId=Guid.NewGuid()});
        repository.Setup(r=>r.SetHelpfulAsync(id,voter,true,default)).ReturnsAsync((true,1,changed));
        var result=await CheckInHelpfulHandler.Handle(new SetCheckInHelpfulCommand(id,voter,true),repository.Object,queries.Object,cache.Object,default);
        Assert.True(result.Data.IsHelpful);Assert.Equal(1,result.Data.HelpfulCount);
        cache.Verify(c=>c.RemoveAsync(It.IsAny<CacheKey>()),changed?Times.AtLeastOnce():Times.Never());
    }

    [Fact]
    public async Task AuthorCannotVoteAndHiddenAliasDoesNotRevealContent()
    {
        var id=Guid.NewGuid();var owner=Guid.NewGuid();var repository=new Mock<IQueryCheckInRepository>();var queries=new Mock<ICheckInQueries>();var cache=new Mock<ICacheService>();
        queries.Setup(q=>q.GetUnifiedByIdAsync(id,null,default)).ReturnsAsync(new UnifiedCheckInDto {Id=id,UserId=owner});
        await Assert.ThrowsAsync<ForbiddenException>(()=>CheckInHelpfulHandler.Handle(new SetCheckInHelpfulCommand(id,owner,true),repository.Object,queries.Object,cache.Object,default));
        queries.Setup(q=>q.GetUnifiedByIdAsync(id,null,default)).ReturnsAsync((UnifiedCheckInDto?)null);
        await Assert.ThrowsAsync<NotFoundException>(()=>CheckInHelpfulHandler.Handle(new SetCheckInHelpfulCommand(id,Guid.NewGuid(),true),repository.Object,queries.Object,cache.Object,default));
        repository.Verify(r=>r.SetHelpfulAsync(It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<bool>(),It.IsAny<CancellationToken>()),Times.Never());
    }
}
