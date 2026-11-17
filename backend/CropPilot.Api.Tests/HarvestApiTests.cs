using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CropPilot.Api.Tests;

/// <summary>透過正式 HTTP 路由驗證採收及整批栽培的刪除限制。</summary>
public sealed class HarvestApiTests(FieldsApiFixture fixture) : IClassFixture<FieldsApiFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>從已結束批次補登採收，驗證清單、篩選、排序與修改後的整批累計。</summary>
    [Fact]
    public async Task CompletedPlantingSupportsHarvestLifecycle()
    {
        using var client = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(client);
        var plantingId = await fixture.AddCompletedPlantingAsync(fieldId);
        var plantingUrl = $"/api/v1/plantings/{plantingId}";
        var harvestsUrl = $"{plantingUrl}/harvests";
        var today = Today;
        var earlier = today.AddDays(-3);
        var empty = await client.GetFromJsonAsync<JsonElement>(harvestsUrl);
        Assert.Equal(0m, empty.GetProperty("totalWeightKg").GetDecimal());
        Assert.Empty(empty.GetProperty("harvests").EnumerateArray());

        using var first = await client.PostAsJsonAsync(harvestsUrl, new { harvestDate = earlier, weightKg = 40m });
        var firstId = (await ReadAsync(first, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var description = string.Concat(Enumerable.Repeat("🍅", 500));
        using var second = await client.PostAsJsonAsync(harvestsUrl, new { harvestDate = today, weightKg = 60m, description });
        var created = await ReadAsync(second, HttpStatusCode.Created);
        var secondId = created.GetProperty("id").GetGuid();
        Assert.Equal(plantingId, created.GetProperty("plantingId").GetGuid());
        Assert.Equal(description, created.GetProperty("description").GetString());

        Assert.Equal(new[] { secondId, firstId }, await ListIdsAsync(client, harvestsUrl));
        Assert.Equal(new[] { firstId, secondId }, await ListIdsAsync(client, $"{harvestsUrl}?sortBy=weightKg&sortDirection=asc"));
        var filtered = await client.GetFromJsonAsync<JsonElement>($"{harvestsUrl}?startDate={today:yyyy-MM-dd}&endDate={today:yyyy-MM-dd}");
        Assert.Equal(100m, filtered.GetProperty("totalWeightKg").GetDecimal());
        Assert.Equal(secondId, Assert.Single(filtered.GetProperty("harvests").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(new[] { firstId }, await ListIdsAsync(client, $"{harvestsUrl}?endDate={earlier:yyyy-MM-dd}"));

        var itemUrl = $"/api/v1/harvests/{secondId}";
        using var edit = await client.PutAsJsonAsync(itemUrl, new { harvestDate = today, weightKg = 35.125m });
        var updated = await ReadAsync(edit, HttpStatusCode.OK);
        Assert.Equal(secondId, updated.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("description").ValueKind);
        Assert.Equal(35.125m, updated.GetProperty("weightKg").GetDecimal());
        Assert.Equal(75.125m, (await client.GetFromJsonAsync<JsonElement>(harvestsUrl)).GetProperty("totalWeightKg").GetDecimal());

        using var deleted = await client.DeleteAsync(itemUrl);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await client.GetFromJsonAsync<JsonElement>(harvestsUrl);
        Assert.Equal(40m, remaining.GetProperty("totalWeightKg").GetDecimal());
        Assert.Equal(firstId, Assert.Single(remaining.GetProperty("harvests").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal("completed", (await client.GetFromJsonAsync<JsonElement>(plantingUrl)).GetProperty("status").GetString());
    }

    /// <summary>同日可記多筆，採收日或重量相同時依契約使用下一個排序欄位。</summary>
    [Fact]
    public async Task HarvestSortingBreaksDateAndWeightTies()
    {
        using var client = (await fixture.CreateAccountClientAsync()).Client;
        var planting = await CreatePlantingAsync(client, await CreateFieldAsync(client), "harvesting");
        var url = $"{planting}/harvests";
        var today = Today;
        using var first = await client.PostAsJsonAsync(url, new { harvestDate = today.AddDays(-1), weightKg = 40 });
        var firstId = (await ReadAsync(first, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using var second = await client.PostAsJsonAsync(url, new { harvestDate = today, weightKg = 60 });
        var secondId = (await ReadAsync(second, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        using var third = await client.PostAsJsonAsync(url, new { harvestDate = today, weightKg = 40 });
        var thirdId = (await ReadAsync(third, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        Assert.Equal(new[] { thirdId, secondId, firstId }, await ListIdsAsync(client, url));
        Assert.Equal(new[] { secondId, thirdId, firstId }, await ListIdsAsync(client, $"{url}?sortBy=weightKg"));
        Assert.Equal(new[] { thirdId, firstId, secondId }, await ListIdsAsync(client, $"{url}?sortBy=weightKg&sortDirection=asc"));
    }

    /// <summary>採收日期晚於台北當日會回傳日期欄位錯誤。</summary>
    [Fact]
    public async Task FutureHarvestDateProducesFieldError()
    {
        using var client = (await fixture.CreateAccountClientAsync()).Client;
        var planting = await CreatePlantingAsync(client, await CreateFieldAsync(client), "harvesting");
        using var response = await client.PostAsJsonAsync($"{planting}/harvests",
            new { harvestDate = Today.AddDays(1), weightKg = 40 });
        Assert.True((await ReadAsync(response, HttpStatusCode.BadRequest))
            .GetProperty("errors").TryGetProperty("harvestDate", out _));
        Assert.Empty(await ListIdsAsync(client, $"{planting}/harvests"));
    }

    /// <summary>規劃中拒絕登記採收，且其他帳號無法使用清單或單筆路由存取採收。</summary>
    [Fact]
    public async Task HarvestsRespectPlantingStatusAndAccountOwnership()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        using var other = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner);
        var planned = await CreatePlantingAsync(owner, fieldId, "planned");
        var growing = await CreatePlantingAsync(owner, fieldId, "growing");
        var payload = new { harvestDate = Today, weightKg = 10m };
        using var skipped = await owner.PostAsJsonAsync($"{planned}/harvests", payload);
        Assert.Equal(HttpStatusCode.Conflict, skipped.StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"{planned}/harvests")).GetProperty("harvests").EnumerateArray());

        var harvestsUrl = $"{growing}/harvests";
        using var forbiddenList = await other.GetAsync(harvestsUrl);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenList.StatusCode);
        using var forbiddenCreate = await other.PostAsJsonAsync(harvestsUrl, payload);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenCreate.StatusCode);
        using var created = await owner.PostAsJsonAsync(harvestsUrl, payload);
        var id = (await ReadAsync(created, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var itemUrl = $"/api/v1/harvests/{id}";
        using var forbiddenEdit = await other.PutAsJsonAsync(itemUrl, payload);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenEdit.StatusCode);
        using var forbiddenDelete = await other.DeleteAsync(itemUrl);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenDelete.StatusCode);
        Assert.Equal(new[] { id }, await ListIdsAsync(owner, harvestsUrl));
    }

    /// <summary>輸入的重量必須是符合範圍與精度的 JSON 數值。</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1000000000")]
    [InlineData("0.0000011")]
    [InlineData("40.000000000000000000000000001")]
    [InlineData("\"40\"")]
    public async Task InvalidHarvestWeightProducesFieldError(string weight)
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var plantingId = await fixture.AddCompletedPlantingAsync(await CreateFieldAsync(owner));
        var harvestsUrl = $"/api/v1/plantings/{plantingId}/harvests";
        using var body = new StringContent($$"""{"harvestDate":"{{Today:yyyy-MM-dd}}","weightKg":{{weight}}}""",
            Encoding.UTF8, "application/json");
        using var response = await owner.PostAsync(harvestsUrl, body);
        Assert.True((await ReadAsync(response, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("weightKg", out _));
        Assert.Empty(await ListIdsAsync(owner, harvestsUrl));
    }

    /// <summary>日期區間和排序參數有誤時，以欄位名稱回報。</summary>
    [Theory]
    [InlineData("startDate=2026-11-17&endDate=2026-11-16", "endDate")]
    [InlineData("sortBy=weight", "sortBy")]
    [InlineData("sortDirection=newest", "sortDirection")]
    [InlineData("sortBy=", "sortBy")]
    [InlineData("sortDirection=%20", "sortDirection")]
    [InlineData("startDate=", "startDate")]
    [InlineData("endDate=11%2F17%2F2026", "endDate")]
    public async Task InvalidHarvestQueryProducesFieldError(string query, string field)
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var plantingId = await fixture.AddCompletedPlantingAsync(await CreateFieldAsync(owner));
        using var response = await owner.GetAsync($"/api/v1/plantings/{plantingId}/harvests?{query}");
        Assert.True((await ReadAsync(response, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty(field, out _));
    }

    /// <summary>採收新增後，栽培刪除預覽與持有舊預覽的刪除請求均回傳阻擋原因。</summary>
    [Fact]
    public async Task ExistingHarvestBlocksPlantingDeleteAndPreview()
    {
        using var client = (await fixture.CreateAccountClientAsync()).Client;
        var planting = await CreatePlantingAsync(client, await CreateFieldAsync(client), "growing");
        var previewUrl = $"{planting}/delete-preview";
        var preview = await client.GetFromJsonAsync<JsonElement>(previewUrl);
        var deleteUrl = $"{planting}?previewVersion={preview.GetProperty("previewVersion").GetString()}";
        var harvestsUrl = $"{planting}/harvests";
        using var harvest = await client.PostAsJsonAsync(harvestsUrl, new { harvestDate = Today, weightKg = 40m });
        var harvestId = (await ReadAsync(harvest, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        using var blockedPreview = await client.GetAsync(previewUrl);
        using var blockedDelete = await client.DeleteAsync(deleteUrl);
        foreach (var response in new[] { blockedPreview, blockedDelete })
        {
            var problem = await ReadAsync(response, HttpStatusCode.Conflict);
            Assert.Contains("已有採收紀錄", problem.GetProperty("detail").GetString());
        }
        Assert.Equal(new[] { harvestId }, await ListIdsAsync(client, harvestsUrl));

        using var removedHarvest = await client.DeleteAsync($"/api/v1/harvests/{harvestId}");
        Assert.Equal(HttpStatusCode.NoContent, removedHarvest.StatusCode);
        using var deletedPlanting = await client.DeleteAsync(deleteUrl);
        Assert.Equal(HttpStatusCode.NoContent, deletedPlanting.StatusCode);
    }

    /// <summary>並行新增採收及刪除栽培時，只會有其中一個操作成功。</summary>
    [Fact]
    public async Task ConcurrentHarvestAndPlantingDeleteKeepConsistentData()
    {
        using var client = (await fixture.CreateAccountClientAsync()).Client;
        var planting = await CreatePlantingAsync(client, await CreateFieldAsync(client), "harvesting");
        var preview = await client.GetFromJsonAsync<JsonElement>($"{planting}/delete-preview");
        var harvestsUrl = $"{planting}/harvests";

        var create = client.PostAsJsonAsync(harvestsUrl, new { harvestDate = Today, weightKg = 40m });
        var delete = client.DeleteAsync($"{planting}?previewVersion={preview.GetProperty("previewVersion").GetString()}");
        await Task.WhenAll(create, delete);
        using var created = await create;
        using var deleted = await delete;
        Assert.True((created.StatusCode, deleted.StatusCode) is
            (HttpStatusCode.Created, HttpStatusCode.Conflict) or (HttpStatusCode.NotFound, HttpStatusCode.NoContent));
        if (created.StatusCode == HttpStatusCode.Created)
        {
            Assert.Single(await ListIdsAsync(client, harvestsUrl));
        }
        else
        {
            using var missing = await client.GetAsync(harvestsUrl);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    /// <summary>用正式田區 API 建立測試田區。</summary>
    private static async Task<Guid> CreateFieldAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/fields",
            new { name = Guid.NewGuid().ToString(), areaSquareMeters = 100 });
        return (await ReadAsync(response, HttpStatusCode.Created)).GetProperty("fields")[0].GetProperty("id").GetGuid();
    }

    /// <summary>透過正式 API 建立指定狀態的批次，回傳單筆 URL。</summary>
    private static async Task<string> CreatePlantingAsync(HttpClient client, Guid fieldId, string status)
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/fields/{fieldId}/plantings", new
        {
            cropName = "番茄", areaSquareMeters = 25, status,
            actualPlantingDate = status == "planned" ? (DateOnly?)null : Today.AddDays(-30)
        });
        await ReadAsync(response, HttpStatusCode.Created);
        return response.Headers.Location!.ToString();
    }

    /// <summary>依回應順序取得清單中的採收識別碼。</summary>
    private static async Task<Guid[]> ListIdsAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("harvests").EnumerateArray()
            .Select(harvest => harvest.GetProperty("id").GetGuid()).ToArray();

    /// <summary>核對狀態並解析 JSON，失敗時保留回應供診斷。</summary>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, received {response.StatusCode}: {content}");
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
