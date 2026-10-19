using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>Pins RestaurantTableService behaviour, including its read-side status refresh side effects.</summary>
public sealed class RestaurantTableTests : TestBase
{
    [Fact]
    public async Task Add_CreatesAvailableActiveTable()
    {
        var dto = await Tables.AddRestaurantTableAsync(new RestaurantTableDto
        {
            TableNumber = "  T-01  ",
            Capacity = 4
        });

        Assert.Equal("T-01", dto.TableNumber);
        Assert.Equal(4, dto.Capacity);
        Assert.True(dto.IsActive);
        Assert.Equal("Available", dto.Status);
    }

    [Fact]
    public async Task Add_Throws_WhenNumberIsBlankOrCapacityIsNotPositive()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Tables.AddRestaurantTableAsync(new RestaurantTableDto { TableNumber = "  ", Capacity = 4 }));
        await Assert.ThrowsAsync<ArgumentException>(() => Tables.AddRestaurantTableAsync(new RestaurantTableDto { TableNumber = "T", Capacity = 0 }));
    }

    [Fact]
    public async Task Add_Throws_WhenTableNumberAlreadyExists()
    {
        await Tables.AddRestaurantTableAsync(new RestaurantTableDto { TableNumber = "DUP", Capacity = 2 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Tables.AddRestaurantTableAsync(new RestaurantTableDto
        {
            TableNumber = "DUP",
            Capacity = 2
        }));

        Assert.Equal("Table number already exists", ex.Message);
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenMissing()
    {
        Assert.Null(await Tables.GetRestaurantTableByIdAsync(999999));
    }

    [Fact]
    public async Task GetById_ComputesOccupied_AndPersistsIt()
    {
        var table = SeedTable();
        var staff = SeedUser();
        SeedOrder(table.TableId, staff.Id, status: "Pending");

        var dto = await Tables.GetRestaurantTableByIdAsync(table.TableId);

        Assert.NotNull(dto);
        Assert.Equal("Occupied", dto!.Status);

        using var fresh = NewContext();
        Assert.Equal("Occupied", fresh.RestaurantTables.Single(x => x.TableId == table.TableId).Status);
    }

    [Fact]
    public async Task GetAll_ComputesStatus_ButDoesNotPersistIt()
    {
        // Baseline: GetAll uses the AsNoTracking repository method, so the recalculated
        // status is returned to the caller but never written back to the database.
        var table = SeedTable();
        var staff = SeedUser();
        SeedOrder(table.TableId, staff.Id, status: "Pending");

        var all = await Tables.GetAllRestaurantTablesAsync();

        var dto = Assert.Single(all);
        Assert.Equal("Occupied", dto.Status);

        using var fresh = NewContext();
        Assert.Equal("Available", fresh.RestaurantTables.Single(x => x.TableId == table.TableId).Status);
    }

    [Fact]
    public async Task StatusIsFree_WhenOnlyPaidCompletedOrCancelledOrdersExist()
    {
        var table = SeedTable();
        var staff = SeedUser();
        SeedOrder(table.TableId, staff.Id, status: "Paid");
        SeedOrder(table.TableId, staff.Id, status: "Completed");
        SeedOrder(table.TableId, staff.Id, status: "Cancelled");

        var dto = await Tables.GetRestaurantTableByIdAsync(table.TableId);

        Assert.Equal("Available", dto!.Status);
    }

    [Fact]
    public async Task RefreshStatusForTable_PersistsOccupied()
    {
        var table = SeedTable();
        var staff = SeedUser();
        SeedOrder(table.TableId, staff.Id, status: "SentToKitchen");

        await Tables.RefreshStatusForTableAsync(table.TableId);

        using var fresh = NewContext();
        Assert.Equal("Occupied", fresh.RestaurantTables.Single(x => x.TableId == table.TableId).Status);
    }

    [Fact]
    public async Task Update_ReturnsFalseWhenMissing_AndUpdatesFields()
    {
        var table = SeedTable(capacity: 2);

        Assert.False(await Tables.UpdateRestaurantTableAsync(999999, new RestaurantTableDto { TableNumber = "X", Capacity = 1 }));

        Assert.True(await Tables.UpdateRestaurantTableAsync(table.TableId, new RestaurantTableDto
        {
            TableNumber = "  Renamed  ",
            Capacity = 8
        }));

        var stored = Db.RestaurantTables.Single(x => x.TableId == table.TableId);
        Assert.Equal("Renamed", stored.TableNumber);
        Assert.Equal(8, stored.Capacity);
    }

    [Fact]
    public async Task SetMaintenance_TogglesActiveAndStatus()
    {
        var table = SeedTable();

        Assert.True(await Tables.SetMaintenanceAsync(table.TableId, enabled: true));
        Assert.False(Db.RestaurantTables.Single(x => x.TableId == table.TableId).IsActive);
        Assert.Equal("Maintenance", Db.RestaurantTables.Single(x => x.TableId == table.TableId).Status);

        Assert.True(await Tables.SetMaintenanceAsync(table.TableId, enabled: false));
        Assert.True(Db.RestaurantTables.Single(x => x.TableId == table.TableId).IsActive);
        Assert.Equal("Available", Db.RestaurantTables.Single(x => x.TableId == table.TableId).Status);

        Assert.False(await Tables.SetMaintenanceAsync(999999, enabled: true));
    }

    [Fact]
    public async Task Delete_SoftDeletesAsMaintenance()
    {
        var table = SeedTable();

        Assert.True(await Tables.DeleteRestaurantTableAsync(table.TableId));

        var stored = Db.RestaurantTables.Single(x => x.TableId == table.TableId);
        Assert.False(stored.IsActive);
        Assert.Equal("Maintenance", stored.Status);
        Assert.False(await Tables.DeleteRestaurantTableAsync(999999));
    }
}
