using System.Text.Json;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Application.Features.Import.IngestImportFile;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Import;

public class ImportFileParserTests
{
    [Fact]
    public void Parse_OsmCandidatesWrapper_ReadsPlaces()
    {
        var json = """
            {
              "total": 2,
              "candidates": [
                {
                  "externalId": "node/1",
                  "name": "Coffe Joy",
                  "lat": 53.9152,
                  "lon": 27.5847,
                  "address": "Немига 5",
                  "phone": "+375 29 111-22-33",
                  "website": "https://coffejoy.by",
                  "instagram": "https://instagram.com/coffejoy",
                  "openingHours": "Mo-Fr 08:00-20:00",
                  "tags": { "amenity": "cafe", "name": "Coffe Joy", "cuisine": "coffee_shop" }
                }
              ]
            }
            """;

        var places = ImportFileParser.Parse(JsonDocument.Parse(json).RootElement);

        places.Should().ContainSingle();
        var place = places[0];
        place.Source.Should().Be(ImportSource.Osm);
        place.Snapshot.ExternalId.Should().Be("node/1");
        place.Snapshot.Name.Should().Be("Coffe Joy");
        place.Snapshot.Phone.Should().Be("+375 29 111-22-33");
        place.Snapshot.Latitude.Should().Be(53.9152m);
    }

    [Fact]
    public void Parse_GeoJson_ReadsFeatureProperties()
    {
        var json = """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "geometry": { "type": "Point", "coordinates": [27.5847, 53.9152] },
                  "properties": { "name": "Kitchen", "address": "Октябрьская 16" }
                }
              ]
            }
            """;

        var places = ImportFileParser.Parse(JsonDocument.Parse(json).RootElement);

        places.Should().ContainSingle();
        places[0].Source.Should().Be(ImportSource.File);
        places[0].Snapshot.Name.Should().Be("Kitchen");
        places[0].Snapshot.Latitude.Should().Be(53.9152m);
        places[0].Snapshot.Longitude.Should().Be(27.5847m);
        places[0].Snapshot.Address.Should().Be("Октябрьская 16");
    }

    [Fact]
    public void Parse_TwoGisItems_ReadsContacts()
    {
        var json = """
            {
              "result": {
                "items": [
                  {
                    "id": "70000001032569327",
                    "name": "7/25 Espresso Bar",
                    "address_name": "просп. Независимости, 18",
                    "point": { "lat": 53.896, "lon": 27.561 },
                    "contact_groups": [
                      {
                        "contacts": [
                          { "type": "phone", "value": "+375 29 111-22-33" },
                          { "type": "website", "value": "https://725.by" },
                          { "type": "instagram", "value": "725espressobar" }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var places = ImportFileParser.Parse(JsonDocument.Parse(json).RootElement);

        places.Should().ContainSingle();
        places[0].Snapshot.Name.Should().Be("7/25 Espresso Bar");
        places[0].Snapshot.Phone.Should().Be("+375 29 111-22-33");
        places[0].Snapshot.Website.Should().Be("https://725.by");
        places[0].Snapshot.Instagram.Should().Be("725espressobar");
    }

    [Fact]
    public void Parse_GooglePlaces_ReadsDisplayName()
    {
        var json = """
            {
              "places": [
                {
                  "id": "ChIJabc",
                  "displayName": { "text": "Marks Coffee Roasters" },
                  "formattedAddress": "Минск",
                  "location": { "latitude": 53.91, "longitude": 27.57 },
                  "nationalPhoneNumber": "+375 29 200-00-00",
                  "websiteUri": "https://marks.by",
                  "googleMapsUri": "https://maps.google.com/?cid=1"
                }
              ]
            }
            """;

        var places = ImportFileParser.Parse(JsonDocument.Parse(json).RootElement);

        places.Should().ContainSingle();
        places[0].Snapshot.Name.Should().Be("Marks Coffee Roasters");
        places[0].Snapshot.Phone.Should().Be("+375 29 200-00-00");
        places[0].GoogleMapsUri.Should().Be("https://maps.google.com/?cid=1");
    }

    [Fact]
    public void Parse_PlainArray_ReadsRows()
    {
        var json = """
            [
              { "name": "BarBerry", "lat": 53.9081, "lon": 27.5294 }
            ]
            """;

        var places = ImportFileParser.Parse(JsonDocument.Parse(json).RootElement);

        places.Should().ContainSingle();
        places[0].Snapshot.Name.Should().Be("BarBerry");
    }

    [Fact]
    public void LooksLikeDecisionsFile_WhenOnlyDecisionMap()
    {
        var json = """
            { "decisions": { "node/1": "specialty" }, "events": [] }
            """;
        var root = JsonDocument.Parse(json).RootElement;

        ImportFileParser.LooksLikeDecisionsFile(root).Should().BeTrue();
        ImportFileParser.Parse(root).Should().BeEmpty();
    }

    [Fact]
    public void Parse_DedupesSameExternalIdInOneFile()
    {
        var json = """
            {
              "candidates": [
                { "externalId": "node/1", "name": "A", "lat": 53.91, "lon": 27.55 },
                { "externalId": "node/1", "name": "A", "lat": 53.91, "lon": 27.55 }
              ]
            }
            """;

        ImportFileParser.Parse(JsonDocument.Parse(json).RootElement).Should().ContainSingle();
    }

    [Fact]
    public void Parse_SpikeCandidatesFile_ReadsOsmPlaces()
    {
        var path = FindRepoFile(".planning/spikes/001-osm-overpass-minsk-cafes/candidates.json");
        if (path is null)
            return;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var places = ImportFileParser.Parse(doc.RootElement);

        places.Count.Should().BeGreaterThan(1000);
        places.Should().OnlyContain(p => p.Source == ImportSource.Osm);
        places.Should().Contain(p => p.Snapshot.Name == "BarBerry");
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
