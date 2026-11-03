using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>HTTP-level coverage cho khu vực bàn và liên kết bàn → khu vực.</summary>
public sealed class TableAreaHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public TableAreaHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TableArea_Crud_AndSoftDelete()
    {
        using var client = _factory.CreateClient();
        var name = $"Khu HTTP {Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/table-areas", new
        {
            areaName = name,
            description = "Khu test",
            displayOrder = 9
        });

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("areaId").GetInt32();
        Assert.Equal(name, created.GetProperty("areaName").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());

        var get = await client.GetAsync($"/api/table-areas/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var row = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(name, row.GetProperty("areaName").GetString());

        var update = await client.PutAsJsonAsync($"/api/table-areas/{id}", new
        {
            areaName = $"{name} updated",
            description = "Đã sửa",
            displayOrder = 10
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var deactivate = await client.PatchAsJsonAsync(
            $"/api/table-areas/{id}/active",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        var activeList = await client.GetFromJsonAsync<JsonElement>("/api/table-areas");
        Assert.DoesNotContain(
            activeList.EnumerateArray(),
            x => x.GetProperty("areaId").GetInt32() == id);

        var allList = await client.GetFromJsonAsync<JsonElement>("/api/table-areas?includeInactive=true");
        Assert.Contains(
            allList.EnumerateArray(),
            x => x.GetProperty("areaId").GetInt32() == id);

        var restore = await client.PatchAsJsonAsync(
            $"/api/table-areas/{id}/active",
            new { isActive = true });
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        var delete = await client.DeleteAsync($"/api/table-areas/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task CreateTable_WithArea_ReturnsAreaName_AndTableListContainsIt()
    {
        using var client = _factory.CreateClient();
        var areaName = $"Khu bàn {Guid.NewGuid():N}";

        var areaResponse = await client.PostAsJsonAsync("/api/table-areas", new
        {
            areaName,
            description = "Khu gắn bàn",
            displayOrder = 1
        });
        Assert.Equal(HttpStatusCode.OK, areaResponse.StatusCode);
        var area = await areaResponse.Content.ReadFromJsonAsync<JsonElement>();
        var areaId = area.GetProperty("areaId").GetInt32();

        var tableNumber = $"HTTP-{Guid.NewGuid():N}"[..20];
        var create = await client.PostAsJsonAsync("/api/restaurant-tables", new
        {
            tableNumber,
            capacity = 4,
            areaId
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var createPayload = await create.Content.ReadFromJsonAsync<JsonElement>();
        var table = createPayload.GetProperty("data");
        Assert.Equal(areaId, table.GetProperty("areaId").GetInt32());
        Assert.Equal(areaName, table.GetProperty("areaName").GetString());

        var list = await client.GetFromJsonAsync<JsonElement>("/api/restaurant-tables");
        var row = list.EnumerateArray()
            .Single(x => x.GetProperty("tableNumber").GetString() == tableNumber);
        Assert.Equal(areaName, row.GetProperty("areaName").GetString());
    }
}
