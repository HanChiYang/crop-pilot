using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace CropPilot.Api.Tests;

/// <summary>從正式 HTTP 路由驗證肥料庫存與施肥作業的一致性。</summary>
public sealed class FertilizationApiTests(FieldsApiFixture fixture) : IClassFixture<FieldsApiFixture>
{
    private const string MaterialsUrl = "/api/v1/fertilizers";
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>驗證已結束栽培的複合施肥補登、退回用量與單價快照。</summary>
    [Fact]
    public async Task CompletedPlantingCanRecordAndEditMultipleMaterials()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        var operationsUrl = await CreateCompletedPlantingAsync(client);
        var material = await CreateMaterialAsync(client, "有機肥");
        var otherMaterial = await CreateMaterialAsync(client, "複合肥");
        var lot = await CreateLotAsync(client, material, 10, 20);
        var otherLot = await CreateLotAsync(client, otherMaterial, 2, null);
        var lotUrl = $"{MaterialsUrl}/{material}/lots/{lot}";

        using var created = await client.PostAsJsonAsync(operationsUrl, new
        {
            operationDate = Today,
            details = new[] { new { materialPurchaseLotId = lot, quantity = 5m },
                new { materialPurchaseLotId = otherLot, quantity = 2m } }
        });
        var operation = await ReadAsync(created, HttpStatusCode.Created);
        var details = operation.GetProperty("details");
        Assert.Equal(JsonValueKind.Null, details[1].GetProperty("unitPrice").ValueKind);
        Assert.Equal("TWD", details[0].GetProperty("currencyCode").GetString());

        using var belowUsed = await client.PutAsJsonAsync(lotUrl, new { receivedQuantity = 4, unitPrice = 30 });
        Assert.Equal(HttpStatusCode.Conflict, belowUsed.StatusCode);
        using var updatedLot = await client.PutAsJsonAsync(lotUrl, new { receivedQuantity = 10, unitPrice = 30 });
        Assert.Equal(HttpStatusCode.OK, updatedLot.StatusCode);
        using var cannotDelete = await client.DeleteAsync(lotUrl);
        Assert.Equal(HttpStatusCode.Conflict, cannotDelete.StatusCode);

        var operationId = operation.GetProperty("id").GetGuid();
        var detailId = details[0].GetProperty("id").GetGuid();
        using var invalidDetail = await client.PutAsJsonAsync($"{operationsUrl}/{operationId}", new
        {
            operationDate = Today,
            details = new[] { new { id = Guid.Empty, materialPurchaseLotId = lot, quantity = 1m } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidDetail.StatusCode);

        using var edited = await client.PutAsJsonAsync($"{operationsUrl}/{operationId}", new
        {
            operationDate = Today,
            details = new object[] { new { id = detailId, materialPurchaseLotId = lot, quantity = 7m },
                new { materialPurchaseLotId = lot, quantity = 1m } }
        });
        var updated = await ReadAsync(edited, HttpStatusCode.OK);
        var updatedDetails = updated.GetProperty("details");
        Assert.Equal(detailId, updatedDetails[0].GetProperty("id").GetGuid());
        Assert.Equal(20m, updatedDetails[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(30m, updatedDetails[1].GetProperty("unitPrice").GetDecimal());

        var inventory = await client.GetFromJsonAsync<JsonElement>(MaterialsUrl);
        Assert.All(inventory.EnumerateArray(), stock => Assert.Equal(2m, stock.GetProperty("remainingQuantity").GetDecimal()));
        var history = await client.GetFromJsonAsync<JsonElement>(operationsUrl);
        Assert.Equal(operationId, history.EnumerateArray().Single().GetProperty("id").GetGuid());
        using var canDelete = await client.DeleteAsync($"{MaterialsUrl}/{otherMaterial}/lots/{otherLot}");
        Assert.Equal(HttpStatusCode.NoContent, canDelete.StatusCode);
    }

    /// <summary>驗證同批次並行施用只會扣量一次，且不同帳號無法讀取或扣用批次。</summary>
    [Fact]
    public async Task ConcurrentUsageCannotOverspendAndOtherAccountsCannotUseLot()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        var (other, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        using var otherClient = other;
        var firstPlanting = await CreateCompletedPlantingAsync(client);
        var secondPlanting = await CreateCompletedPlantingAsync(client);
        var foreignPlanting = await CreateCompletedPlantingAsync(otherClient);
        var material = await CreateMaterialAsync(client, "有機肥");
        var lot = await CreateLotAsync(client, material, 5, 10);
        var request = new
        {
            operationDate = Today,
            details = new[] { new { materialPurchaseLotId = lot, quantity = 4m } }
        };

        using var foreignLots = await otherClient.GetAsync($"{MaterialsUrl}/{material}/lots");
        Assert.Equal(HttpStatusCode.NotFound, foreignLots.StatusCode);
        using var foreignUsage = await otherClient.PostAsJsonAsync(foreignPlanting, request);
        Assert.Equal(HttpStatusCode.NotFound, foreignUsage.StatusCode);

        var writes = await Task.WhenAll(client.PostAsJsonAsync(firstPlanting, request),
            client.PostAsJsonAsync(secondPlanting, request));
        using var accepted = Assert.Single(writes, response => response.StatusCode == HttpStatusCode.Created);
        using var rejected = Assert.Single(writes, response => response.StatusCode == HttpStatusCode.Conflict);
        var problem = await ReadAsync(rejected, HttpStatusCode.Conflict);
        Assert.True(problem.GetProperty("errors").TryGetProperty("details[0].quantity", out _));
        var stock = await client.GetFromJsonAsync<JsonElement>($"{MaterialsUrl}/{material}/lots");
        Assert.Equal(1m, stock[0].GetProperty("remainingQuantity").GetDecimal());
    }

    /// <summary>驗證資材及進貨批次更正，並限制仍有批次時修改單位或刪除資材。</summary>
    [Fact]
    public async Task MaterialUnitAndDeletionDependOnExistingLots()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        var materialId = await CreateMaterialAsync(client, "有機肥");
        var materialUrl = $"{MaterialsUrl}/{materialId}";
        var lotsUrl = $"{materialUrl}/lots";

        using var duplicate = await client.PostAsJsonAsync(MaterialsUrl, new { name = " 有機肥 ", stockUnit = "公斤" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var badPrecision = await client.PostAsJsonAsync(lotsUrl,
            new { source = "purchase", purchasedOn = Today, receivedQuantity = 0.0000011m });
        Assert.Equal(HttpStatusCode.BadRequest, badPrecision.StatusCode);

        const decimal maximum = 999999999.999999m;
        const decimal exclusiveMaximum = 1000000000m;
        using var excessiveQuantity = await client.PostAsJsonAsync(lotsUrl,
            new { source = "purchase", purchasedOn = Today, receivedQuantity = exclusiveMaximum });
        Assert.Equal(HttpStatusCode.BadRequest, excessiveQuantity.StatusCode);
        using var excessivePrice = await client.PostAsJsonAsync(lotsUrl,
            new { source = "purchase", purchasedOn = Today, receivedQuantity = 10m, unitPrice = exclusiveMaximum });
        Assert.Equal(HttpStatusCode.BadRequest, excessivePrice.StatusCode);

        using var purchase = await client.PostAsJsonAsync(lotsUrl,
            new { source = "purchase", purchasedOn = Today, receivedQuantity = maximum, unitPrice = maximum });
        var lot = await ReadAsync(purchase, HttpStatusCode.Created);
        var lotId = lot.GetProperty("id").GetGuid();
        Assert.Equal(Today.ToString("yyyy-MM-dd"), lot.GetProperty("availableFrom").GetString());
        Assert.Equal(maximum, lot.GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(maximum, lot.GetProperty("unitPrice").GetDecimal());

        using var rename = await client.PutAsJsonAsync(materialUrl, new { name = "有機肥二號", stockUnit = "公斤" });
        var renamed = await ReadAsync(rename, HttpStatusCode.OK);
        Assert.Equal("有機肥二號", renamed.GetProperty("name").GetString());
        Assert.Equal(maximum, renamed.GetProperty("remainingQuantity").GetDecimal());
        using var changeUnit = await client.PutAsJsonAsync(materialUrl, new { name = "有機肥二號", stockUnit = "袋" });
        Assert.Equal(HttpStatusCode.Conflict, changeUnit.StatusCode);
        using var inUse = await client.DeleteAsync(materialUrl);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        using var deleteLot = await client.DeleteAsync($"{lotsUrl}/{lotId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteLot.StatusCode);
        using var deleteMaterial = await client.DeleteAsync(materialUrl);
        Assert.Equal(HttpStatusCode.NoContent, deleteMaterial.StatusCode);
    }

    /// <summary>建立測試田區及可補登施肥的已結束栽培。</summary>
    private async Task<string> CreateCompletedPlantingAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/fields", new
        {
            name = Guid.NewGuid().ToString(), areaSquareMeters = 100
        });
        var field = await ReadAsync(response, HttpStatusCode.Created);
        var fieldId = field.GetProperty("fields")[0].GetProperty("id").GetGuid();
        var plantingId = await fixture.AddCompletedPlantingAsync(fieldId);
        return $"/api/v1/fields/{fieldId}/plantings/{plantingId}/fertilizations";
    }

    /// <summary>建立測試資材並取得識別碼。</summary>
    private static async Task<Guid> CreateMaterialAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(MaterialsUrl, new { name, stockUnit = "公斤" });
        return (await ReadAsync(response, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    /// <summary>建立期初庫存並確認生效日與來源的契約格式。</summary>
    private static async Task<Guid> CreateLotAsync(HttpClient client, Guid material, decimal quantity, decimal? price)
    {
        using var response = await client.PostAsJsonAsync($"{MaterialsUrl}/{material}/lots",
            new { source = "openingStock", receivedQuantity = quantity, unitPrice = price });
        var lot = await ReadAsync(response, HttpStatusCode.Created);
        Assert.Equal("openingStock", lot.GetProperty("source").GetString());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), lot.GetProperty("availableFrom").GetString());
        return lot.GetProperty("id").GetGuid();
    }

    /// <summary>確認 HTTP 狀態並解析回應，錯誤時保留回應內容供診斷。</summary>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, received {response.StatusCode}: {content}");
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
