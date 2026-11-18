using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace CropPilot.Api.Tests;

/// <summary>驗證資料庫保存的列舉代碼、作業明細的類型約束及稽核時間。</summary>
public sealed class SchemaConventionsTests(FieldsApiFixture fixture) : IClassFixture<FieldsApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>API 維持小寫契約值，資料庫一律保存大寫代碼，農藥單位的顯示文字由 API 轉換。</summary>
    [Fact]
    public async Task EnumValuesAreStoredAsUppercaseCodes()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        using var fieldResponse = await client.PostAsJsonAsync("/api/v1/fields",
            new { name = Guid.NewGuid().ToString(), areaSquareMeters = 100 });
        var fieldId = (await ReadAsync(fieldResponse, HttpStatusCode.Created))
            .GetProperty("fields")[0].GetProperty("id").GetGuid();
        // 未結束的栽培才能取得刪除預覽。
        using var plantingResponse = await client.PostAsJsonAsync($"/api/v1/fields/{fieldId}/plantings",
            new { cropName = "番茄", areaSquareMeters = 10, status = "growing", actualPlantingDate = Today });
        var plantingId = (await ReadAsync(plantingResponse, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        using var pesticideResponse = await client.PostAsJsonAsync("/api/v1/pesticides",
            new { name = "代碼農藥", stockUnit = "liter", package = new { quantity = 1m, unit = "liter" } });
        var pesticide = await ReadAsync(pesticideResponse, HttpStatusCode.Created);
        Assert.Equal("liter", pesticide.GetProperty("stockUnit").GetString());
        Assert.Equal("liter", pesticide.GetProperty("package").GetProperty("unit").GetString());
        var pesticideId = pesticide.GetProperty("id").GetGuid();
        Assert.Equal("LITER LITER", await fixture.QuerySingleAsync<string>($"""
            SELECT stock_unit || ' ' || package_unit AS "Value" FROM agricultural_materials WHERE id = {pesticideId}
            """));

        using var lotResponse = await client.PostAsJsonAsync($"/api/v1/pesticides/{pesticideId}/lots",
            new { source = "openingStock", receivedQuantity = 5m });
        var lotId = (await ReadAsync(lotResponse, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using var applicationResponse = await client.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/material-applications", new
        {
            operationDate = Today, applicationMethod = "spray",
            details = new[] { new { materialPurchaseLotId = lotId, quantity = 1m } }
        });
        var application = await ReadAsync(applicationResponse, HttpStatusCode.Created);
        Assert.Equal("spray", application.GetProperty("applicationMethod").GetString());
        Assert.Equal("公升", application.GetProperty("details")[0].GetProperty("unit").GetString());
        var applicationId = application.GetProperty("id").GetGuid();
        Assert.Equal("SPRAY", await fixture.QuerySingleAsync<string>($"""
            SELECT application_method AS "Value" FROM material_applications WHERE field_operation_id = {applicationId}
            """));
        using var previewResponse = await client.GetAsync($"/api/v1/plantings/{plantingId}/delete-preview");
        var preview = await ReadAsync(previewResponse, HttpStatusCode.OK);
        Assert.Equal("公升", preview.GetProperty("inventoryReturns")[0].GetProperty("unit").GetString());

        using var irrigationResponse = await client.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/irrigations",
            new { operationDate = Today, method = "sprinkler" });
        var irrigation = await ReadAsync(irrigationResponse, HttpStatusCode.Created);
        Assert.Equal("sprinkler", irrigation.GetProperty("method").GetString());
        var irrigationId = irrigation.GetProperty("id").GetGuid();
        Assert.Equal("SPRINKLER", await fixture.QuerySingleAsync<string>($"""
            SELECT method AS "Value" FROM irrigation_details WHERE field_operation_id = {irrigationId}
            """));
    }

    /// <summary>作業明細只能接到相同類型的田間作業，作業類型建立後也不能改變。</summary>
    [Fact]
    public async Task OperationDetailsMustMatchOperationType()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        var plantingId = await CreateCompletedPlantingAsync(client);
        using var irrigationResponse = await client.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/irrigations",
            new { operationDate = Today, method = "drip" });
        var irrigationId = (await ReadAsync(irrigationResponse, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var foreignKey = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync($"""
            INSERT INTO material_applications (id, field_operation_id, operation_type, application_method)
            VALUES ({Guid.NewGuid()}, {irrigationId}, 'MATERIAL_APPLICATION', 'BROADCAST')
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignKey.SqlState);
        var check = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync($"""
            INSERT INTO material_applications (id, field_operation_id, operation_type, application_method)
            VALUES ({Guid.NewGuid()}, {irrigationId}, 'IRRIGATION', 'BROADCAST')
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, check.SqlState);
        var typeChange = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync($"""
            UPDATE field_operations SET operation_type = 'MATERIAL_APPLICATION' WHERE id = {irrigationId}
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, typeChange.SqlState);
    }

    /// <summary>修改田區、栽培、資材及批次時更新修改時間，建立時間維持不變。</summary>
    [Fact]
    public async Task EditsRefreshUpdatedAt()
    {
        var (owner, _) = await fixture.CreateAccountClientAsync();
        using var client = owner;
        using var fieldResponse = await client.PostAsJsonAsync("/api/v1/fields",
            new { name = Guid.NewGuid().ToString(), areaSquareMeters = 100 });
        var fieldId = (await ReadAsync(fieldResponse, HttpStatusCode.Created))
            .GetProperty("fields")[0].GetProperty("id").GetGuid();
        using var plantingResponse = await client.PostAsJsonAsync($"/api/v1/fields/{fieldId}/plantings",
            new { cropName = "番茄", areaSquareMeters = 10, status = "planned" });
        var plantingId = (await ReadAsync(plantingResponse, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using var materialResponse = await client.PostAsJsonAsync("/api/v1/fertilizers", new { name = "堆肥", stockUnit = "公斤" });
        var materialId = (await ReadAsync(materialResponse, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using var lotResponse = await client.PostAsJsonAsync($"/api/v1/fertilizers/{materialId}/lots",
            new { source = "openingStock", receivedQuantity = 5m });
        var lotId = (await ReadAsync(lotResponse, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var rows = new (string Table, Guid Id)[]
        {
            ("fields", fieldId), ("plantings", plantingId), ("agricultural_materials", materialId), ("material_purchase_lots", lotId)
        };
        var before = new Dictionary<string, (DateTimeOffset Created, DateTimeOffset Updated)>();
        foreach (var (table, id) in rows)
        {
            before[table] = await ReadTimestampsAsync(table, id);
            Assert.Equal(before[table].Created, before[table].Updated);
        }

        // updated_at 由應用程式時鐘寫入，與資料庫預設值的時鐘可能不同，只和編輯前的應用程式時間比較。
        var editStartedAt = DateTimeOffset.UtcNow;
        using var fieldEdit = await client.PutAsJsonAsync($"/api/v1/fields/{fieldId}",
            new { name = Guid.NewGuid().ToString(), areaSquareMeters = 100 });
        Assert.Equal(HttpStatusCode.OK, fieldEdit.StatusCode);
        using var plantingEdit = await client.PutAsJsonAsync($"/api/v1/plantings/{plantingId}",
            new { cropName = "番茄", areaSquareMeters = 12, status = "planned" });
        Assert.Equal(HttpStatusCode.OK, plantingEdit.StatusCode);
        using var materialEdit = await client.PutAsJsonAsync($"/api/v1/fertilizers/{materialId}",
            new { name = "腐熟堆肥", stockUnit = "公斤" });
        Assert.Equal(HttpStatusCode.OK, materialEdit.StatusCode);
        using var lotEdit = await client.PutAsJsonAsync($"/api/v1/fertilizer-lots/{lotId}",
            new { availableFrom = Today, receivedQuantity = 6m, unitPrice = 10m });
        Assert.Equal(HttpStatusCode.OK, lotEdit.StatusCode);

        foreach (var (table, id) in rows)
        {
            var after = await ReadTimestampsAsync(table, id);
            Assert.Equal(before[table].Created, after.Created);
            Assert.True(after.Updated >= editStartedAt, $"{table}.updated_at was not refreshed.");
        }
    }

    /// <summary>讀取指定資料列的建立與修改時間。</summary>
    /// <param name="table">測試內固定清單中的資料表名稱，不是外部輸入。</param>
    /// <param name="id">資料列識別碼。</param>
    private async Task<(DateTimeOffset Created, DateTimeOffset Updated)> ReadTimestampsAsync(string table, Guid id)
    {
        var created = await fixture.QuerySingleAsync<DateTimeOffset>(
            FormattableStringFactory.Create($"SELECT created_at AS \"Value\" FROM {table} WHERE id = {{0}}", id));
        var updated = await fixture.QuerySingleAsync<DateTimeOffset>(
            FormattableStringFactory.Create($"SELECT updated_at AS \"Value\" FROM {table} WHERE id = {{0}}", id));
        return (created, updated);
    }

    /// <summary>建立田區及今日結束的栽培，回傳栽培識別碼。</summary>
    private async Task<Guid> CreateCompletedPlantingAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/fields",
            new { name = Guid.NewGuid().ToString(), areaSquareMeters = 100 });
        var fieldId = (await ReadAsync(response, HttpStatusCode.Created)).GetProperty("fields")[0].GetProperty("id").GetGuid();
        return await fixture.AddCompletedPlantingAsync(fieldId);
    }

    /// <summary>確認狀態碼並解析回應內容。</summary>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, received {response.StatusCode}: {content}");
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
