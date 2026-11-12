using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace CropPilot.Api.Tests;

/// <summary>從正式 HTTP 路由驗證灌溉作業與帳號隔離。</summary>
public sealed class IrrigationApiTests(FieldsApiFixture fixture) : IClassFixture<FieldsApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>已結束的栽培可補登、排序、清除用水量及刪除。</summary>
    [Fact]
    public async Task CompletedPlantingSupportsIrrigationLifecycle()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        var fieldId = await CreateFieldAsync(client);
        var plantingId = await fixture.AddCompletedPlantingAsync(fieldId);
        var url = $"/api/v1/plantings/{plantingId}/irrigations";
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());

        var description = string.Concat(Enumerable.Repeat("🌱", 500));
        using var created = await client.PostAsJsonAsync(url, new
        {
            operationDate = Today, method = "drip", waterVolumeLiters = 999999999.999999m, description
        });
        var record = await ReadAsync(created, HttpStatusCode.Created);
        var id = record.GetProperty("id").GetGuid();
        Assert.Equal("drip", record.GetProperty("method").GetString());
        Assert.Equal(999999999.999999m, record.GetProperty("waterVolumeLiters").GetDecimal());
        Assert.Equal(description, record.GetProperty("description").GetString());
        using var wrongType = await client.PutAsJsonAsync($"/api/v1/fertilizations/{id}", new
        {
            operationDate = Today,
            details = new[] { new { materialPurchaseLotId = Guid.NewGuid(), quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.NotFound, wrongType.StatusCode);

        var itemUrl = $"/api/v1/irrigations/{id}";

        using var older = await client.PostAsJsonAsync(url, new { operationDate = Today.AddDays(-30), method = "manual" });
        var olderId = (await ReadAsync(older, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var list = await client.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal(new[] { id, olderId }, list.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));

        using var edited = await client.PutAsJsonAsync(itemUrl, new
        {
            operationDate = Today, method = "furrow", waterVolumeLiters = (decimal?)null
        });
        var updated = await ReadAsync(edited, HttpStatusCode.OK);
        Assert.Equal(id, updated.GetProperty("id").GetGuid());
        Assert.Equal("furrow", updated.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("waterVolumeLiters").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("description").ValueKind);

        var deletions = await Task.WhenAll(client.DeleteAsync(itemUrl), client.DeleteAsync(itemUrl));
        using var first = deletions[0];
        using var second = deletions[1];
        Assert.Equal(new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound },
            deletions.Select(response => response.StatusCode).Order());
        list = await client.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal(olderId, list.EnumerateArray().Single().GetProperty("id").GetGuid());
        var planting = await client.GetFromJsonAsync<JsonElement>($"/api/v1/plantings/{plantingId}");
        Assert.Equal("completed", planting.GetProperty("status").GetString());
    }

    /// <summary>規劃中的栽培可查看空清單，新增會回傳衝突。</summary>
    [Fact]
    public async Task PlannedPlantingCannotRecordIrrigation()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        var fieldId = await CreateFieldAsync(client);
        using var plantingResponse = await client.PostAsJsonAsync($"/api/v1/fields/{fieldId}/plantings",
            new { cropName = "番茄", areaSquareMeters = 25, status = "planned" });
        var plantingId = (await ReadAsync(plantingResponse, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var url = $"/api/v1/plantings/{plantingId}/irrigations";
        using var denied = await client.PostAsJsonAsync(url, new { operationDate = Today, method = "drip" });
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());
    }

    /// <summary>跨帳號不可查詢或寫入，且欄位錯誤不會產生作業。</summary>
    [Fact]
    public async Task OwnershipAndFieldValidationApplyToShallowRoutes()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        var (other, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        using var outsider = other;
        var fieldId = await CreateFieldAsync(client);
        var plantingId = await fixture.AddCompletedPlantingAsync(fieldId);
        var url = $"/api/v1/plantings/{plantingId}/irrigations";
        var valid = new { operationDate = Today, method = "sprinkler" };
        using var deniedList = await outsider.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, deniedList.StatusCode);
        using var deniedCreate = await outsider.PostAsJsonAsync(url, valid);
        Assert.Equal(HttpStatusCode.NotFound, deniedCreate.StatusCode);

        foreach (var liters in new[] { 0m, -1m, 0.0000011m, 1_000_000_000m })
        {
            using var invalid = await client.PostAsJsonAsync(url,
                new { operationDate = Today, method = "sprinkler", waterVolumeLiters = liters });
            Assert.True((await ReadAsync(invalid, HttpStatusCode.BadRequest)).GetProperty("errors")
                .TryGetProperty("waterVolumeLiters", out _));
        }
        using var future = await client.PostAsJsonAsync(url, new { operationDate = Today.AddDays(1), method = "drip" });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        using var missingMethod = await client.PostAsJsonAsync(url, new { operationDate = Today });
        Assert.Equal(HttpStatusCode.BadRequest, missingMethod.StatusCode);
        foreach (var description in new[] { "\0", new string('a', 501) })
        {
            using var invalid = await client.PostAsJsonAsync(url,
                new { operationDate = Today, method = "drip", description });
            Assert.True((await ReadAsync(invalid, HttpStatusCode.BadRequest)).GetProperty("errors")
                .TryGetProperty("description", out _));
        }
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());

        using var created = await client.PostAsJsonAsync(url, valid);
        var id = (await ReadAsync(created, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var itemUrl = $"/api/v1/irrigations/{id}";
        using var deniedEdit = await outsider.PutAsJsonAsync(itemUrl, valid);
        Assert.Equal(HttpStatusCode.NotFound, deniedEdit.StatusCode);
        using var deniedDelete = await outsider.DeleteAsync(itemUrl);
        Assert.Equal(HttpStatusCode.NotFound, deniedDelete.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());
    }

    /// <summary>透過正式田區端點建立測試田區。</summary>
    private static async Task<Guid> CreateFieldAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/fields",
            new { name = Guid.NewGuid().ToString(), areaSquareMeters = 100 });
        return (await ReadAsync(response, HttpStatusCode.Created)).GetProperty("fields")[0].GetProperty("id").GetGuid();
    }

    /// <summary>核對 HTTP 狀態並解析 JSON，失敗時保留回應供診斷。</summary>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, received {response.StatusCode}: {content}");
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
