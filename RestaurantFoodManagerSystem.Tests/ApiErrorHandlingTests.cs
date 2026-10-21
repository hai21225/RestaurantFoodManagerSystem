using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantFoodManagerSystem.Infrastructure.Errors;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Đợt 1 (lỗi an toàn) — mọi lỗi trả về cho client phải có status code đúng và body
/// <c>{ "message": "..." }</c>; lỗi hệ thống không được lộ ra ngoài.
/// </summary>
public sealed class ApiErrorHandlingTests
{
    private static ExceptionContext BuildContext(Exception exception)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ExceptionContext(actionContext, new List<IFilterMetadata>())
        {
            Exception = exception
        };
    }

    private static (int Status, string Message) Apply(Exception exception)
    {
        var filter = new ApiExceptionFilter(NullLogger<ApiExceptionFilter>.Instance);
        var context = BuildContext(exception);
        filter.OnException(context);

        Assert.True(context.ExceptionHandled);

        var result = Assert.IsType<ObjectResult>(context.Result);
        var message = Assert.IsType<ApiExceptionFilter.ErrorResponse>(result.Value).Message;

        return (result.StatusCode!.Value, message!);
    }

    // ==========================================================
    // Ánh xạ exception -> status code
    // ==========================================================

    [Fact]
    public void KeyNotFound_Becomes404()
    {
        var (status, message) = Apply(new KeyNotFoundException("Food not found"));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal("Food not found", message);
    }

    [Fact]
    public void ArgumentException_Becomes400()
    {
        var (status, message) = Apply(new ArgumentException("Quantity must be positive"));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("Quantity must be positive", message);
    }

    [Fact]
    public void InvalidOperation_Becomes409()
    {
        var (status, message) = Apply(new InvalidOperationException("Topping already exists"));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("Topping already exists", message);
    }

    [Fact]
    public void UnknownException_Becomes500_WithGenericMessage()
    {
        var (status, message) = Apply(new InvalidCastException("secret connection string"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);

        // Message thô của lỗi hệ thống KHÔNG được lộ ra client.
        Assert.DoesNotContain("secret connection string", message);
    }

    // ==========================================================
    // Hợp đồng HTTP thực tế
    // ==========================================================

    [Fact]
    public async Task MissingFood_Returns404_WithMessageBody()
    {
        using var factory = new RestaurantApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/foods/999999/sizes");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Food not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DuplicateTopping_Returns409_WithMessageBody()
    {
        using var factory = new RestaurantApiFactory();
        using var client = factory.CreateClient();

        var name = $"Topping {Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/toppings", new { toppingName = name, toppingPrice = 5m });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/toppings", new { toppingName = name, toppingPrice = 5m });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Topping already exists", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MissingCategoryOnCreateFood_Returns404_WithMessageBody()
    {
        using var factory = new RestaurantApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/foods", new
        {
            categoryId = 999999,
            foodName = "Ghost",
            foodDescription = "d",
            foodImageUrl = "img.png",
            foodPrice = 10m
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Category not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MissingTableOnCreateOrder_Returns404_WithMessageBody()
    {
        using var factory = new RestaurantApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            tableId = 999999,
            createdBy = 1,
            items = new[] { new { sizeId = 1, quantity = 1, toppings = Array.Empty<object>() } }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Table not found", body.GetProperty("message").GetString());
    }
}
