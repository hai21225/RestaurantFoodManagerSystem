using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>Pins khu vực bàn: CRUD, soft-delete và liên kết với bàn.</summary>
public sealed class TableAreaTests : TestBase
{
    [Fact]
    public async Task Create_Update_Delete_AndRestore_Area()
    {
        var created = await Areas.CreateAsync(new TableAreaRequest
        {
            AreaName = "  Sảnh A  ",
            Description = "  Tầng trệt  ",
            DisplayOrder = 1
        });

        Assert.Equal("Sảnh A", created.AreaName);
        Assert.Equal("Tầng trệt", created.Description);
        Assert.True(created.IsActive);

        Assert.True(await Areas.UpdateAsync(created.AreaId, new TableAreaRequest
        {
            AreaName = "Sảnh A1",
            Description = "Mô tả mới",
            DisplayOrder = 2
        }));

        var updated = await Areas.GetByIdAsync(created.AreaId);
        Assert.Equal("Sảnh A1", updated!.AreaName);
        Assert.Equal(2, updated.DisplayOrder);

        Assert.True(await Areas.DeleteAsync(created.AreaId, default));
        Assert.DoesNotContain(
            await Areas.GetAllAsync(includeInactive: false),
            x => x.AreaId == created.AreaId);
        Assert.Contains(
            await Areas.GetAllAsync(includeInactive: true),
            x => x.AreaId == created.AreaId);

        Assert.True(await Areas.SetActiveAsync(created.AreaId, true, default));
        Assert.Contains(
            await Areas.GetAllAsync(includeInactive: false),
            x => x.AreaId == created.AreaId);
    }

    [Fact]
    public async Task Create_RejectsDuplicateActiveName()
    {
        var area = SeedTableArea("Trùng tên");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Areas.CreateAsync(new TableAreaRequest { AreaName = "  Trùng tên  " }));

        Assert.Contains("already exists", ex.Message);
        Assert.NotEqual(0, area.AreaId);
    }

    [Fact]
    public async Task AddTable_WithArea_ReturnsAreaName()
    {
        var area = SeedTableArea("Lầu 2");

        var dto = await Tables.AddRestaurantTableAsync(new RestaurantTableDto
        {
            TableNumber = "L2-01",
            Capacity = 4,
            AreaId = area.AreaId
        });

        Assert.Equal(area.AreaId, dto.AreaId);
        Assert.Equal("Lầu 2", dto.AreaName);

        var all = await Tables.GetAllRestaurantTablesAsync();
        var stored = all.Single(x => x.TableNumber == "L2-01");
        Assert.Equal("Lầu 2", stored.AreaName);
    }

    [Fact]
    public async Task AddTable_RejectsInactiveArea()
    {
        var area = SeedTableArea("Khu đóng", isActive: false);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tables.AddRestaurantTableAsync(new RestaurantTableDto
            {
                TableNumber = "X-01",
                Capacity = 2,
                AreaId = area.AreaId
            }));

        Assert.Contains("inactive", ex.Message);
    }

    [Fact]
    public async Task GetAllTables_KeepsSoftDeletedTable_AsMaintenance()
    {
        var visible = SeedTable("A-01");
        var deleted = SeedTable("A-02");
        deleted.IsActive = false;
        deleted.Status = RestaurantTableStatuses.Maintenance;
        Db.SaveChanges();

        var all = await Tables.GetAllRestaurantTablesAsync();

        Assert.Contains(all, x => x.TableId == visible.TableId);
        var hidden = all.Single(x => x.TableId == deleted.TableId);
        Assert.False(hidden.IsActive);
        Assert.Equal("Maintenance", hidden.Status);
    }

    [Fact]
    public async Task SetMaintenance_ActiveTable_IsShownAsMaintenance_ButRemainsActive()
    {
        var table = SeedTable("M-01");

        Assert.True(await Tables.SetMaintenanceAsync(table.TableId, enabled: true));

        var stored = await Db.RestaurantTables.AsNoTracking()
            .SingleAsync(x => x.TableId == table.TableId);
        Assert.True(stored.IsActive);
        Assert.Equal("Maintenance", stored.Status);

        var all = await Tables.GetAllRestaurantTablesAsync();
        Assert.Equal("Maintenance", all.Single(x => x.TableId == table.TableId).Status);

        Assert.True(await Tables.SetMaintenanceAsync(table.TableId, enabled: false));
        Assert.Equal("Available", (await Tables.GetAllRestaurantTablesAsync())
            .Single(x => x.TableId == table.TableId).Status);
    }
}
