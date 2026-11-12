using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;
using Xunit;

namespace CropPilot.Api.Tests;

/// <summary>透過正式 HTTP 路由驗證栽培契約、帳號範圍及資料庫刪除限制。</summary>
public sealed class PlantingsApiTests(FieldsApiFixture fixture) : IClassFixture<FieldsApiFixture>
{
    private const string FieldsUrl = "/api/v1/fields";
    private const string OverviewUrl = "/api/v1/overview";
    private const string HistoryUrl = "/api/v1/planting-history";

    /// <summary>驗證歷史清單、組合篩選、選項去重排序及帳號隔離。</summary>
    [Fact]
    public async Task ListsOnlyOwnedCompletedHistoryAndCropOptions()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        using var other = (await fixture.CreateAccountClientAsync()).Client;
        using var admin = (await fixture.CreateAccountClientAsync(UserRole.Admin)).Client;
        using var anonymous = fixture.CreateClient();
        var north = await CreateFieldAsync(owner, "北側田");
        var south = await CreateFieldAsync(owner, "南側田");
        var empty = await CreateFieldAsync(owner, "空田");
        var foreign = await CreateFieldAsync(other, "其他帳號");
        var older = await fixture.AddCompletedPlantingAsync(north, "Tomato");
        var recent = await fixture.AddCompletedPlantingAsync(north, "Tomato", new DateOnly(2026, 11, 2));
        var sameDate = await fixture.AddCompletedPlantingAsync(north, "tomato");
        var southRecord = await fixture.AddCompletedPlantingAsync(south, "Beans", new DateOnly(2026, 11, 3));
        await fixture.AddCompletedPlantingAsync(foreign, "Private");
        using var current = await owner.PostAsJsonAsync($"{FieldsUrl}/{north}/plantings",
            new { cropName = "Current", areaSquareMeters = 25 });
        Assert.Equal(HttpStatusCode.Created, current.StatusCode);

        var all = (await owner.GetFromJsonAsync<PlantingHistoryItemDto[]>(HistoryUrl))!;
        Assert.Equal(new[] { southRecord, recent }.Concat(new[] { older, sameDate }.Order()),
            all.Select(row => row.Id));
        var filtered = await owner.GetFromJsonAsync<JsonElement>($"{HistoryUrl}?fieldId={north}&cropName=Tomato");
        Assert.Equal(new[] { recent, older }, filtered.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
        Assert.Equal("北側田", filtered[0].GetProperty("fieldName").GetString());
        Assert.Equal("2026-11-02", filtered[0].GetProperty("actualPlantingDate").GetString());
        Assert.Equal(25m, filtered[0].GetProperty("areaSquareMeters").GetDecimal());
        Assert.Equal(JsonValueKind.Null, filtered[0].GetProperty("variety").ValueKind);
        Assert.Equal(sameDate, Assert.Single((await owner.GetFromJsonAsync<PlantingHistoryItemDto[]>(
            $"{HistoryUrl}?cropName=tomato"))!).Id);
        Assert.Empty((await owner.GetFromJsonAsync<PlantingHistoryItemDto[]>(
            $"{HistoryUrl}?fieldId={south}&cropName=Tomato"))!);

        Assert.Equal(new[] { "Beans", "Tomato", "tomato" },
            await owner.GetFromJsonAsync<string[]>($"{HistoryUrl}/crops"));
        Assert.Equal(new[] { "Tomato", "tomato" },
            await owner.GetFromJsonAsync<string[]>($"{HistoryUrl}/crops?fieldId={north}"));
        Assert.Empty((await owner.GetFromJsonAsync<string[]>($"{HistoryUrl}/crops?fieldId={empty}"))!);
        Assert.Empty((await owner.GetFromJsonAsync<PlantingHistoryItemDto[]>($"{HistoryUrl}?fieldId={empty}"))!);

        foreach (var url in new[] { HistoryUrl, $"{HistoryUrl}/crops" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{url}?fieldId={foreign}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{url}?fieldId=")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{url}?fieldId=invalid")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{HistoryUrl}?cropName=%20")).StatusCode);
    }

    /// <summary>驗證作物選項可直接用作查詢參數，且以 Unicode 碼點驗證字數。</summary>
    [Fact]
    public async Task FiltersByCropNamesContainingSupplementaryCharacters()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var cropName = string.Concat(Enumerable.Repeat("𠮷", 120));
        var plantingId = await fixture.AddCompletedPlantingAsync(fieldId, cropName);

        var options = (await owner.GetFromJsonAsync<string[]>($"{HistoryUrl}/crops?fieldId={fieldId}"))!;
        var option = Assert.Single(options);
        Assert.Equal(cropName, option);
        var rows = (await owner.GetFromJsonAsync<PlantingHistoryItemDto[]>(
            $"{HistoryUrl}?cropName={Uri.EscapeDataString(option)}"))!;
        Assert.Equal(plantingId, Assert.Single(rows).Id);
        using var tooLong = await owner.GetAsync($"{HistoryUrl}?cropName={Uri.EscapeDataString(option + "𠮷")}");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    /// <summary>驗證三種初始狀態、可空日期及新增後的讀取路徑。</summary>
    [Theory]
    [InlineData("""{"areaSquareMeters":25,"cropName":" 番茄 "}""", "planned", null)]
    [InlineData("""{"areaSquareMeters":25,"cropName":" 番茄 ","status":"harvesting","actualPlantingDate":null}""", "harvesting", null)]
    [InlineData("""{"areaSquareMeters":25,"cropName":" 番茄 ","status":"growing","actualPlantingDate":"2026-11-01"}""", "growing", "2026-11-01")]
    public async Task CreatesPlantingAndReadsItFromLocationListAndOverview(string json, string status, string? date)
    {
        var (client, _) = await fixture.CreateAccountClientAsync();
        using var owner = client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var url = $"{FieldsUrl}/{fieldId}/plantings";
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("plantings").EnumerateArray());
        using var request = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await owner.PostAsync(url, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var planting = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plantingId = planting.GetProperty("id").GetGuid();
        Assert.Equal(fieldId, planting.GetProperty("fieldId").GetGuid());
        Assert.Equal("番茄", planting.GetProperty("cropName").GetString());
        Assert.Equal(status, planting.GetProperty("status").GetString());
        Assert.Equal(date, planting.GetProperty("actualPlantingDate").GetString());
        Assert.Null(planting.GetProperty("plannedPlantingDate").GetString());
        Assert.Null(planting.GetProperty("variety").GetString());
        Assert.Null(planting.GetProperty("notes").GetString());
        Assert.Equal($"/api/v1/plantings/{plantingId}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(plantingId, (await owner.GetFromJsonAsync<JsonElement>(response.Headers.Location)).GetProperty("id").GetGuid());
        Assert.Equal(plantingId, Assert.Single((await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("plantings").EnumerateArray()).GetProperty("id").GetGuid());
        var overview = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(OverviewUrl)).EnumerateArray());
        Assert.Equal(fieldId, overview.GetProperty("id").GetGuid());
        Assert.Equal(plantingId, Assert.Single(overview.GetProperty("plantings").EnumerateArray()).GetProperty("id").GetGuid());
    }

    /// <summary>驗證無效的狀態或日期回傳欄位錯誤且沒有新增資料。</summary>
    [Theory]
    [InlineData("""{"areaSquareMeters":25,"cropName":"番茄","status":"growing"}""", "actualPlantingDate")]
    [InlineData("""{"areaSquareMeters":25,"cropName":"番茄","status":"growing","actualPlantingDate":null}""", "actualPlantingDate")]
    [InlineData("""{"areaSquareMeters":25,"cropName":"番茄","status":"completed"}""", "status")]
    [InlineData("""{"areaSquareMeters":25,"cropName":"番茄","status":3}""", "status")]
    [InlineData("""{"areaSquareMeters":25,"cropName":"番茄","status":null}""", "status")]
    [InlineData("""{"cropName":"番茄"}""", "areaSquareMeters")]
    [InlineData("""{"areaSquareMeters":null,"cropName":"番茄"}""", "areaSquareMeters")]
    [InlineData("""{"areaSquareMeters":0,"cropName":"番茄"}""", "areaSquareMeters")]
    [InlineData("""{"areaSquareMeters":0.00001,"cropName":"番茄"}""", "areaSquareMeters")]
    public async Task RejectsInvalidInitialStateWithoutSaving(string json, string field)
    {
        var (client, _) = await fixture.CreateAccountClientAsync();
        using var owner = client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var url = $"{FieldsUrl}/{fieldId}/plantings";
        using var request = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await owner.PostAsync(url, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("plantings").EnumerateArray());
    }

    /// <summary>驗證缺少角色與跨帳號存取的回應，以及單筆紀錄須屬於路徑指定的田區。</summary>
    [Fact]
    public async Task EnforcesRoleAndFieldOwnership()
    {
        using var anonymous = fixture.CreateClient();
        using var admin = (await fixture.CreateAccountClientAsync(UserRole.Admin)).Client;
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        using var other = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var url = $"{FieldsUrl}/{fieldId}/plantings";
        var request = new { cropName = "番茄", areaSquareMeters = 25 };
        using var created = await owner.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(OverviewUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(created.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync(created.Headers.Location,
            new { cropName = "番茄", areaSquareMeters = 25, status = "planned" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(created.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/plantings/{Guid.NewGuid()}")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>(OverviewUrl)).EnumerateArray());
        Assert.Single((await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("plantings").EnumerateArray());
    }

    /// <summary>驗證有栽培紀錄時單筆及批次刪除均被拒絕，包含只有已結束紀錄的田區。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectsFieldsWithCurrentOrCompletedPlantings(bool completed)
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var protectedField = await CreateFieldAsync(owner, "北側田");
        var otherField = await CreateFieldAsync(owner, "南側田");
        var fieldUrl = $"{FieldsUrl}/{protectedField}";
        if (completed)
        {
            var completedId = await fixture.AddCompletedPlantingAsync(protectedField);
            var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/plantings/{completedId}");
            Assert.Equal("completed", detail.GetProperty("status").GetString());
            Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"{fieldUrl}/plantings")).GetProperty("plantings").EnumerateArray());
            var overview = await owner.GetFromJsonAsync<JsonElement>(OverviewUrl);
            var field = Assert.Single(overview.EnumerateArray(), entry => entry.GetProperty("id").GetGuid() == protectedField);
            Assert.Empty(field.GetProperty("plantings").EnumerateArray());
        }
        else
        {
            using var created = await owner.PostAsJsonAsync($"{fieldUrl}/plantings", new { cropName = "番茄", areaSquareMeters = 25 });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var single = await owner.DeleteAsync(fieldUrl);
        Assert.Equal(HttpStatusCode.Conflict, single.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{FieldsUrl}/bulk-delete")
        {
            Content = JsonContent.Create(new { fieldIds = new[] { otherField, protectedField } })
        };
        using var batch = await owner.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, batch.StatusCode);
        Assert.Equal(2, (await owner.GetFromJsonAsync<FieldRsDto>(FieldsUrl))!.Fields.Count);
    }

    /// <summary>驗證新增栽培與刪除田區並行時，不會留下孤立紀錄或遺失已建立的紀錄。</summary>
    [Fact]
    public async Task ConcurrentCreateAndFieldDeleteLeaveAConsistentResult()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var fieldUrl = $"{FieldsUrl}/{fieldId}";
        var createTask = owner.PostAsJsonAsync($"{fieldUrl}/plantings", new { cropName = "番茄", areaSquareMeters = 25 });
        var deleteTask = owner.DeleteAsync(fieldUrl);
        await Task.WhenAll(createTask, deleteTask);
        using var created = await createTask;
        using var deleted = await deleteTask;

        if (created.StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
            Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"{fieldUrl}/plantings")).GetProperty("plantings").EnumerateArray());
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(OverviewUrl)).EnumerateArray());
        }
    }

    /// <summary>驗證占用上限、整筆編輯、狀態變更與刪除釋放面積。</summary>
    [Fact]
    public async Task EnforcesCapacityAndReleasesItWhenPlantingsAreCompletedOrDeleted()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var fieldUrl = $"{FieldsUrl}/{fieldId}";
        var url = $"{fieldUrl}/plantings";
        using var tomato = await owner.PostAsJsonAsync(url, new
        {
            cropName = "番茄", areaSquareMeters = 70, status = "growing", actualPlantingDate = "2026-11-01"
        });
        Assert.Equal(HttpStatusCode.Created, tomato.StatusCode);
        using var overflow = await owner.PostAsJsonAsync(url, new { cropName = "小白菜", areaSquareMeters = 35 });
        Assert.Equal(HttpStatusCode.Conflict, overflow.StatusCode);
        Assert.Contains("30 m²", (await overflow.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors").GetProperty("areaSquareMeters")[0].GetString());
        using var cabbage = await owner.PostAsJsonAsync(url, new { cropName = "小白菜", areaSquareMeters = 30 });
        Assert.Equal(HttpStatusCode.Created, cabbage.StatusCode);

        using var invalid = await owner.PutAsJsonAsync(cabbage.Headers.Location,
            new { cropName = "改名", areaSquareMeters = 30, status = "growing" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var unchanged = await owner.GetFromJsonAsync<JsonElement>(cabbage.Headers.Location);
        Assert.Equal("小白菜", unchanged.GetProperty("cropName").GetString());
        var updatedCropName = string.Concat(Enumerable.Repeat("🌱", 61));
        using var edited = await owner.PutAsJsonAsync(cabbage.Headers.Location,
            new { cropName = updatedCropName, areaSquareMeters = 30, status = "planned", variety = "示意品種" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(updatedCropName, (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cropName").GetString());
        using var tooSmall = await owner.PutAsJsonAsync(fieldUrl, new { name = "北側田", areaSquareMeters = 99 });
        Assert.Equal(HttpStatusCode.Conflict, tooSmall.StatusCode);
        Assert.Equal(100, (await owner.GetFromJsonAsync<JsonElement>(fieldUrl)).GetProperty("areaSquareMeters").GetDecimal());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(cabbage.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync(tomato.Headers.Location)).StatusCode);
        Assert.Equal(30, (await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("remainingAreaSquareMeters").GetDecimal());
        using var completed = await owner.PutAsJsonAsync(tomato.Headers.Location, new
        {
            cropName = "番茄", areaSquareMeters = 70, status = "completed", actualPlantingDate = "2026-11-01"
        });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(100, (await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("remainingAreaSquareMeters").GetDecimal());
        Assert.Equal("completed", (await owner.GetFromJsonAsync<JsonElement>(tomato.Headers.Location)).GetProperty("status").GetString());
        var overview = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(OverviewUrl)).EnumerateArray());
        Assert.Empty(overview.GetProperty("plantings").EnumerateArray());
    }

    /// <summary>並行縮小田區及新增占用時，只有一筆寫入可成功。</summary>
    [Fact]
    public async Task ConcurrentResizeAndCreateCannotOverallocate()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var fieldUrl = $"{FieldsUrl}/{fieldId}";
        var url = $"{fieldUrl}/plantings";
        var resize = owner.PutAsJsonAsync(fieldUrl, new { name = "北側田", areaSquareMeters = 60 });
        var create = owner.PostAsJsonAsync(url, new { cropName = "番茄", areaSquareMeters = 70 });
        await Task.WhenAll(resize, create);
        using var resized = await resize;
        using var created = await create;
        Assert.True((resized.StatusCode, created.StatusCode) is
            (HttpStatusCode.OK, HttpStatusCode.Conflict) or (HttpStatusCode.Conflict, HttpStatusCode.Created));
        var state = await owner.GetFromJsonAsync<JsonElement>(url);
        Assert.InRange(state.GetProperty("allocatedAreaSquareMeters").GetDecimal(), 0,
            state.GetProperty("areaSquareMeters").GetDecimal());
    }

    /// <summary>透過田區 API 建立測試用田區。</summary>
    private static async Task<Guid> CreateFieldAsync(HttpClient owner, string name)
    {
        using var response = await owner.PostAsJsonAsync(FieldsUrl, new { name, areaSquareMeters = 100 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<FieldRsDto>())!.Fields).Id;
    }
}
