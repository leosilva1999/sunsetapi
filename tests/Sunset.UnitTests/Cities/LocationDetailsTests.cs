using System.Text.Json;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Validators.Locations;
using Sunset.Application.Validators.Moderation;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Cities;

public class LocationDetailsTests
{
    [Fact]
    public void ADescription_IsTrimmed_BlankMeansNone_AndTooLongIsRefused()
    {
        var location = new Location("Praia", -28, -48, "Imbituba", description: "  Bonita.  ");
        Assert.Equal("Bonita.", location.Description);

        location.SetDescription("   ");
        Assert.Null(location.Description);

        Assert.Throws<ArgumentException>(() => location.SetDescription(new string('a', Location.DescriptionMaxLength + 1)));
        location.SetDescription(new string('a', Location.DescriptionMaxLength));
    }

    [Fact]
    public void ACategory_MustBeAKnownValue()
    {
        var location = new Location("Praia", -28, -48, "Imbituba", category: LocationCategory.Lake);
        Assert.Equal(LocationCategory.Lake, location.Category);

        Assert.Throws<ArgumentOutOfRangeException>(() => location.SetCategory((LocationCategory)99));
        location.SetCategory(null);
        Assert.Null(location.Category);
    }

    [Fact]
    public void Category_TravelsAsItsNameInJson_BothWays_AndMayBeAbsent()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var request = JsonSerializer.Deserialize<CreateLocationRequest>("""{"name":"P","latitude":1,"longitude":2,"city":"X","category":"Viewpoint"}""", options)!;
        Assert.Equal(LocationCategory.Viewpoint, request.Category);
        Assert.Null(JsonSerializer.Deserialize<CreateLocationRequest>("""{"name":"P","latitude":1,"longitude":2,"city":"X"}""", options)!.Category);

        var location = new Location("P", 1, 2, "X", category: LocationCategory.Beach, description: "d");
        var json = JsonSerializer.Serialize(location.ToResponse(), options);
        Assert.Contains("\"category\":\"Beach\"", json);
        Assert.Contains("\"description\":\"d\"", json);
        Assert.Contains("\"category\":null", JsonSerializer.Serialize(new Location("P", 1, 2, "X").ToResponse(), options));
    }

    [Fact]
    public void TheValidators_LimitTheDescription_AndTheEditNeedsSomethingToChange()
    {
        var create = new CreateLocationRequestValidator();
        Assert.True(create.Validate(new CreateLocationRequest("P", 1, 2, "X", Description: "ok", Category: LocationCategory.Urban)).IsValid);
        Assert.False(create.Validate(new CreateLocationRequest("P", 1, 2, "X", Description: new string('a', 1001))).IsValid);
        Assert.False(create.Validate(new CreateLocationRequest("P", 1, 2, "X", Category: (LocationCategory)99)).IsValid);

        var edit = new UpdateLocationDetailsRequestValidator();
        Assert.False(edit.Validate(new UpdateLocationDetailsRequest()).IsValid);
        Assert.True(edit.Validate(new UpdateLocationDetailsRequest(Description: "")).IsValid);
        Assert.True(edit.Validate(new UpdateLocationDetailsRequest(Category: LocationCategory.Other)).IsValid);
        Assert.False(edit.Validate(new UpdateLocationDetailsRequest(Description: new string('a', 1001))).IsValid);
    }
}
