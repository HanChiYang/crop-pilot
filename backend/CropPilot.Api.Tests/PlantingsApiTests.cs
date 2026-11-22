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

    /// <summary>結束栽培時須提供介於實際栽種日與台北當日之間的結束日，其他狀態不得填寫。</summary>
    [Fact]
    public async Task CompletingPlantingRequiresValidCompletionDate()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        using var created = await owner.PostAsJsonAsync($"{FieldsUrl}/{fieldId}/plantings", new
        {
            cropName = "番茄", areaSquareMeters = 25, status = "growing", actualPlantingDate = "2026-11-01"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plantingUrl = created.Headers.Location!;
        Assert.Equal(JsonValueKind.Null,
            (await owner.GetFromJsonAsync<JsonElement>(plantingUrl)).GetProperty("completionDate").ValueKind);

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(1).ToString("yyyy-MM-dd");
        foreach (var (status, completionDate) in new (string, string?)[]
        {
            ("completed", null), ("completed", "2026-10-31"), ("completed", tomorrow), ("growing", "2026-11-05")
        })
        {
            using var rejected = await owner.PutAsJsonAsync(plantingUrl, new
            {
                cropName = "番茄", areaSquareMeters = 25, status, actualPlantingDate = "2026-11-01", completionDate
            });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.GetProperty("errors").TryGetProperty("completionDate", out _), problem.GetRawText());
        }
        Assert.Equal("growing", (await owner.GetFromJsonAsync<JsonElement>(plantingUrl)).GetProperty("status").GetString());

        using var completed = await owner.PutAsJsonAsync(plantingUrl, new
        {
            cropName = "番茄", areaSquareMeters = 25, status = "completed", actualPlantingDate = "2026-11-01",
            completionDate = "2026-11-01"
        });
        var saved = await completed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("2026-11-01", saved.GetProperty("completionDate").GetString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        using var other = await owner.PostAsJsonAsync($"{FieldsUrl}/{fieldId}/plantings", new
        {
            cropName = "甘藍", areaSquareMeters = 25, status = "growing", actualPlantingDate = "2026-11-01"
        });
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        using var completedToday = await owner.PutAsJsonAsync(other.Headers.Location, new
        {
            cropName = "甘藍", areaSquareMeters = 25, status = "completed", actualPlantingDate = "2026-11-01",
            completionDate = today.ToString("yyyy-MM-dd")
        });
        Assert.Equal(HttpStatusCode.OK, completedToday.StatusCode);
        var history = (await owner.GetFromJsonAsync<PlantingHistoryItemDto[]>(HistoryUrl))!;
        Assert.Equal(new[] { new DateOnly(2026, 11, 1), today },
            history.Select(item => item.CompletionDate).Order().ToArray());
    }

    /// <summary>結束日不得早於最後一筆採收或作業；結束後補登的採收與作業不得晚於結束日。</summary>
    [Fact]
    public async Task CompletionDateBoundsRecordedActivity()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        using var created = await owner.PostAsJsonAsync($"{FieldsUrl}/{fieldId}/plantings", new
        {
            cropName = "番茄", areaSquareMeters = 25, status = "growing", actualPlantingDate = "2026-11-01"
        });
        var plantingUrl = created.Headers.Location!;
        var plantingId = Guid.Parse(plantingUrl.AbsolutePath.Split('/')[^1]);
        using var harvest = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/harvests",
            new { harvestDate = "2026-11-08", weightKg = 10 });
        Assert.Equal(HttpStatusCode.Created, harvest.StatusCode);
        var harvestId = (await harvest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var irrigation = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/irrigations",
            new { operationDate = "2026-11-10", method = "drip" });
        Assert.Equal(HttpStatusCode.Created, irrigation.StatusCode);

        object Completion(string date) => new
        {
            cropName = "番茄", areaSquareMeters = 25, status = "completed", actualPlantingDate = "2026-11-01",
            completionDate = date
        };
        using var tooEarly = await owner.PutAsJsonAsync(plantingUrl, Completion("2026-11-09"));
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        var conflict = await tooEarly.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("2026-11-10", conflict.GetProperty("errors").GetProperty("completionDate")[0].GetString());
        Assert.Equal("growing", (await owner.GetFromJsonAsync<JsonElement>(plantingUrl)).GetProperty("status").GetString());
        using var completed = await owner.PutAsJsonAsync(plantingUrl, Completion("2026-11-10"));
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        using var material = await owner.PostAsJsonAsync("/api/v1/fertilizers", new { name = "有機肥", stockUnit = "公斤" });
        var materialId = (await material.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var lot = await owner.PostAsJsonAsync($"/api/v1/fertilizers/{materialId}/lots",
            new { source = "openingStock", receivedQuantity = 5m, availableFrom = "2026-11-01" });
        Assert.Equal(HttpStatusCode.Created, lot.StatusCode);
        var lotId = (await lot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var writes = new (HttpMethod Method, string Url, object Body, string Field)[]
        {
            (HttpMethod.Post, $"/api/v1/plantings/{plantingId}/harvests", new { harvestDate = "2026-11-11", weightKg = 5 }, "harvestDate"),
            (HttpMethod.Put, $"/api/v1/harvests/{harvestId}", new { harvestDate = "2026-11-11", weightKg = 10 }, "harvestDate"),
            (HttpMethod.Post, $"/api/v1/plantings/{plantingId}/irrigations", new { operationDate = "2026-11-11", method = "drip" }, "operationDate"),
            (HttpMethod.Post, $"/api/v1/plantings/{plantingId}/material-applications", new
            {
                operationDate = "2026-11-11", applicationMethod = "broadcast",
                details = new[] { new { materialPurchaseLotId = lotId, quantity = 1m } }
            }, "operationDate"),
        };
        foreach (var (method, url, body, field) in writes)
        {
            using var late = await owner.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) });
            Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
            var problem = await late.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
        }
        using var onEndDate = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/harvests",
            new { harvestDate = "2026-11-10", weightKg = 5 });
        Assert.Equal(HttpStatusCode.Created, onEndDate.StatusCode);

        // 採收為最後活動時同樣限制結束日。
        using var other = await owner.PostAsJsonAsync($"{FieldsUrl}/{fieldId}/plantings", new
        {
            cropName = "甘藍", areaSquareMeters = 25, status = "harvesting", actualPlantingDate = "2026-11-01"
        });
        var otherId = Guid.Parse(other.Headers.Location!.AbsolutePath.Split('/')[^1]);
        using var lateHarvest = await owner.PostAsJsonAsync($"/api/v1/plantings/{otherId}/harvests",
            new { harvestDate = "2026-11-12", weightKg = 3 });
        Assert.Equal(HttpStatusCode.Created, lateHarvest.StatusCode);
        using var beforeHarvest = await owner.PutAsJsonAsync(other.Headers.Location, new
        {
            cropName = "甘藍", areaSquareMeters = 25, status = "completed", actualPlantingDate = "2026-11-01",
            completionDate = "2026-11-11"
        });
        Assert.Equal(HttpStatusCode.Conflict, beforeHarvest.StatusCode);
        Assert.Contains("2026-11-12", (await beforeHarvest.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors").GetProperty("completionDate")[0].GetString());
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
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{created.Headers.Location}/delete-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{created.Headers.Location}?previewVersion={new string('A', 64)}")).StatusCode);
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

        var preview = await owner.GetFromJsonAsync<JsonElement>($"{cabbage.Headers.Location}/delete-preview");
        Assert.Equal(30m, preview.GetProperty("releasedAreaSquareMeters").GetDecimal());
        Assert.Equal(0, preview.GetProperty("materialApplicationCount").GetInt32());
        Assert.Empty(preview.GetProperty("inventoryReturns").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(
            $"{cabbage.Headers.Location}?previewVersion={preview.GetProperty("previewVersion").GetString()}")).StatusCode);
        var tomatoPreview = await owner.GetFromJsonAsync<JsonElement>($"{tomato.Headers.Location}/delete-preview");
        Assert.Equal(30, (await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("remainingAreaSquareMeters").GetDecimal());
        using var completed = await owner.PutAsJsonAsync(tomato.Headers.Location, new
        {
            cropName = "番茄", areaSquareMeters = 70, status = "completed", actualPlantingDate = "2026-11-01",
            completionDate = "2026-11-10"
        });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(100, (await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("remainingAreaSquareMeters").GetDecimal());
        Assert.Equal("completed", (await owner.GetFromJsonAsync<JsonElement>(tomato.Headers.Location)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync($"{tomato.Headers.Location}/delete-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync(
            $"{tomato.Headers.Location}?previewVersion={tomatoPreview.GetProperty("previewVersion").GetString()}")).StatusCode);
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

    /// <summary>確認預覽過期時作業不會被刪除，重新確認後則一併釋放用量及面積。</summary>
    [Fact]
    public async Task ConfirmedPlantingDeletionRestoresInventoryAndReleasesArea()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "南側田");
        var fieldPlantings = $"{FieldsUrl}/{fieldId}/plantings";
        using var created = await owner.PostAsJsonAsync(fieldPlantings, new { cropName = "小白菜", areaSquareMeters = 40 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var planting = created.Headers.Location!.ToString();
        var previewUrl = $"{planting}/delete-preview";
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        using var material = await owner.PostAsJsonAsync("/api/v1/fertilizers", new { name = "有機肥", stockUnit = "公斤" });
        var materialId = (await material.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var lotsUrl = $"/api/v1/fertilizers/{materialId}/lots";
        using var lot = await owner.PostAsJsonAsync(lotsUrl, new { source = "openingStock", receivedQuantity = 10m });
        var lotId = (await lot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var preview = await owner.GetFromJsonAsync<JsonElement>(previewUrl);
        using var first = await owner.PostAsJsonAsync($"{planting}/material-applications", new
        {
            operationDate = date, applicationMethod = "broadcast",
            details = new[] { new { materialPurchaseLotId = lotId, quantity = 2m } }
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await owner.PostAsJsonAsync($"{planting}/material-applications", new
        {
            operationDate = date, applicationMethod = "spray",
            details = new[] { new { materialPurchaseLotId = lotId, quantity = 3m } }
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var irrigation = await owner.PostAsJsonAsync($"{planting}/irrigations", new { operationDate = date, method = "drip" });
        Assert.Equal(HttpStatusCode.Created, irrigation.StatusCode);
        using var stale = await owner.DeleteAsync($"{planting}?previewVersion={preview.GetProperty("previewVersion").GetString()}");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(5m, (await owner.GetFromJsonAsync<JsonElement>(lotsUrl))[0].GetProperty("remainingQuantity").GetDecimal());

        preview = await owner.GetFromJsonAsync<JsonElement>(previewUrl);
        Assert.Equal("planned", preview.GetProperty("status").GetString());
        Assert.Equal(2, preview.GetProperty("materialApplicationCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("irrigationCount").GetInt32());
        var restored = Assert.Single(preview.GetProperty("inventoryReturns").EnumerateArray());
        Assert.Equal(lotId, restored.GetProperty("lotId").GetGuid());
        Assert.Equal(5m, restored.GetProperty("quantity").GetDecimal());
        Assert.Equal("openingStock", restored.GetProperty("source").GetString());

        using var deleted = await owner.DeleteAsync($"{planting}?previewVersion={preview.GetProperty("previewVersion").GetString()}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(planting)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{planting}/irrigations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{planting}/material-applications")).StatusCode);
        Assert.Equal(10m, (await owner.GetFromJsonAsync<JsonElement>(lotsUrl))[0].GetProperty("remainingQuantity").GetDecimal());
        Assert.Equal(100m, (await owner.GetFromJsonAsync<JsonElement>(fieldPlantings))
            .GetProperty("remainingAreaSquareMeters").GetDecimal());
    }

    /// <summary>刪除與同筆栽培的新增作業並行時，確認版本不會漏掉新作業。</summary>
    [Fact]
    public async Task ConcurrentIrrigationAndPlantingDeleteRespectPreview()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        using var created = await owner.PostAsJsonAsync($"{FieldsUrl}/{fieldId}/plantings",
            new { cropName = "番茄", areaSquareMeters = 25, status = "growing",
                actualPlantingDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)) });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var planting = created.Headers.Location!.ToString();
        var preview = await owner.GetFromJsonAsync<JsonElement>($"{planting}/delete-preview");
        var delete = owner.DeleteAsync($"{planting}?previewVersion={preview.GetProperty("previewVersion").GetString()}");
        var createIrrigation = owner.PostAsJsonAsync($"{planting}/irrigations",
            new { operationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)), method = "drip" });
        await Task.WhenAll(delete, createIrrigation);
        using var deleted = await delete;
        using var irrigation = await createIrrigation;
        Assert.True((deleted.StatusCode, irrigation.StatusCode) is
            (HttpStatusCode.NoContent, HttpStatusCode.NotFound) or (HttpStatusCode.Conflict, HttpStatusCode.Created));
    }

    /// <summary>驗證結束日可填範圍的四種來源、早於栽種日的作業、同日優先序與上限。</summary>
    [Fact]
    public async Task CompletionDateRangeReflectsAllSourcesAndTieBreakPriority()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var actualPlantingDate = today.AddDays(-20);
        var completionDate = today.AddDays(-2);
        var plantingId = await CreateCompletedPlantingAsync(owner, fieldId, actualPlantingDate, completionDate);
        var rangeUrl = $"/api/v1/plantings/{plantingId}/completion-date-range";

        using var material = await owner.PostAsJsonAsync("/api/v1/fertilizers", new { name = "有機肥", stockUnit = "公斤" });
        Assert.Equal(HttpStatusCode.Created, material.StatusCode);
        var materialId = (await material.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var lot = await owner.PostAsJsonAsync($"/api/v1/fertilizers/{materialId}/lots", new
        {
            source = "openingStock",
            receivedQuantity = 20m,
            availableFrom = today.AddDays(-30)
        });
        Assert.Equal(HttpStatusCode.Created, lot.StatusCode);
        var lotId = (await lot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        async Task AssertRangeAsync(DateOnly expectedEarliest, string expectedSource)
        {
            using var response = await owner.GetAsync(rangeUrl);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var range = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(completionDate.ToString("yyyy-MM-dd"), range.GetProperty("completionDate").GetString());
            Assert.Equal(expectedEarliest.ToString("yyyy-MM-dd"), range.GetProperty("earliestDate").GetString());
            Assert.Equal(expectedSource, range.GetProperty("earliestDateSource").GetString());
            Assert.Equal(today.ToString("yyyy-MM-dd"), range.GetProperty("latestDate").GetString());
        }

        // 尚無任何採收與作業紀錄時，下限為實際栽種日。
        await AssertRangeAsync(actualPlantingDate, "actualPlanting");

        // 早於實際栽種日的灌溉不影響下限。
        using var beforePlantingIrrigation = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/irrigations",
            new { operationDate = actualPlantingDate.AddDays(-1), method = "drip" });
        Assert.Equal(HttpStatusCode.Created, beforePlantingIrrigation.StatusCode);
        await AssertRangeAsync(actualPlantingDate, "actualPlanting");

        // 與實際栽種日同天時，優先順序為 harvest > materialApplication > irrigation > actualPlanting。
        using var tieIrrigation = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/irrigations",
            new { operationDate = actualPlantingDate, method = "drip" });
        Assert.Equal(HttpStatusCode.Created, tieIrrigation.StatusCode);
        await AssertRangeAsync(actualPlantingDate, "irrigation");

        using var tieMaterial = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/material-applications", new
        {
            operationDate = actualPlantingDate,
            applicationMethod = "broadcast",
            details = new[] { new { materialPurchaseLotId = lotId, quantity = 1m } }
        });
        Assert.Equal(HttpStatusCode.Created, tieMaterial.StatusCode);
        await AssertRangeAsync(actualPlantingDate, "materialApplication");

        using var tieHarvest = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/harvests",
            new { harvestDate = actualPlantingDate, weightKg = 2m });
        Assert.Equal(HttpStatusCode.Created, tieHarvest.StatusCode);
        await AssertRangeAsync(actualPlantingDate, "harvest");

        // 較晚的資材施用、灌溉與採收各自成為新的下限。
        var laterMaterialDate = actualPlantingDate.AddDays(5);
        using var laterMaterial = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/material-applications", new
        {
            operationDate = laterMaterialDate,
            applicationMethod = "broadcast",
            details = new[] { new { materialPurchaseLotId = lotId, quantity = 1m } }
        });
        Assert.Equal(HttpStatusCode.Created, laterMaterial.StatusCode);
        await AssertRangeAsync(laterMaterialDate, "materialApplication");

        var laterIrrigationDate = actualPlantingDate.AddDays(8);
        using var laterIrrigation = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/irrigations",
            new { operationDate = laterIrrigationDate, method = "sprinkler" });
        Assert.Equal(HttpStatusCode.Created, laterIrrigation.StatusCode);
        await AssertRangeAsync(laterIrrigationDate, "irrigation");

        var laterHarvestDate = actualPlantingDate.AddDays(10);
        using var laterHarvest = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/harvests",
            new { harvestDate = laterHarvestDate, weightKg = 3m });
        Assert.Equal(HttpStatusCode.Created, laterHarvest.StatusCode);
        await AssertRangeAsync(laterHarvestDate, "harvest");
    }

    /// <summary>驗證修正成功會同步歷史與詳細資料、未變更保留原修改時間，並限制後續補登上限。</summary>
    [Fact]
    public async Task EditCompletionDateUpdatesHistoryPreservesUnchangedTimestampAndBoundsBackfills()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var planted = today.AddDays(-20);
        var planned = today.AddDays(-25);
        var initialCompletion = today.AddDays(-5);
        var plantingId = await CreateCompletedPlantingAsync(
            owner, fieldId, planted, initialCompletion, planned, "番茄", "玉女", "採收備註");
        var editCompletionUrl = $"/api/v1/plantings/{plantingId}/completion-date";

        await fixture.ExecuteAsync($"""
            UPDATE plantings SET updated_at = CURRENT_TIMESTAMP - interval '5 minutes' WHERE id = {plantingId}
            """);
        var beforeUpdatedAt = await fixture.QuerySingleAsync<DateTimeOffset>($"""
            SELECT updated_at AS "Value" FROM plantings WHERE id = {plantingId}
            """);

        using var unchanged = await owner.PutAsJsonAsync(editCompletionUrl, new { completionDate = initialCompletion });
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        var unchangedBody = await unchanged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(initialCompletion.ToString("yyyy-MM-dd"), unchangedBody.GetProperty("completionDate").GetString());
        Assert.Equal("completed", unchangedBody.GetProperty("status").GetString());
        Assert.Equal("玉女", unchangedBody.GetProperty("variety").GetString());
        Assert.Equal("採收備註", unchangedBody.GetProperty("notes").GetString());
        var unchangedUpdatedAt = await fixture.QuerySingleAsync<DateTimeOffset>($"""
            SELECT updated_at AS "Value" FROM plantings WHERE id = {plantingId}
            """);
        Assert.Equal(beforeUpdatedAt, unchangedUpdatedAt);

        var editStartedAt = DateTimeOffset.UtcNow;
        var shortened = today.AddDays(-10);
        using var edited = await owner.PutAsJsonAsync(editCompletionUrl, new { completionDate = shortened });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedBody = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(shortened.ToString("yyyy-MM-dd"), editedBody.GetProperty("completionDate").GetString());
        Assert.Equal("completed", editedBody.GetProperty("status").GetString());
        Assert.Equal("番茄", editedBody.GetProperty("cropName").GetString());
        Assert.Equal("玉女", editedBody.GetProperty("variety").GetString());
        Assert.Equal("採收備註", editedBody.GetProperty("notes").GetString());
        Assert.Equal(planned.ToString("yyyy-MM-dd"), editedBody.GetProperty("plannedPlantingDate").GetString());
        Assert.Equal(planted.ToString("yyyy-MM-dd"), editedBody.GetProperty("actualPlantingDate").GetString());
        Assert.Equal(25m, editedBody.GetProperty("areaSquareMeters").GetDecimal());

        var afterUpdatedAt = await fixture.QuerySingleAsync<DateTimeOffset>($"""
            SELECT updated_at AS "Value" FROM plantings WHERE id = {plantingId}
            """);
        Assert.True(afterUpdatedAt >= editStartedAt && afterUpdatedAt > beforeUpdatedAt);

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/plantings/{plantingId}");
        Assert.Equal(shortened.ToString("yyyy-MM-dd"), detail.GetProperty("completionDate").GetString());
        var historyItem = Assert.Single((await owner.GetFromJsonAsync<PlantingHistoryItemDto[]>(HistoryUrl))!);
        Assert.Equal(shortened, historyItem.CompletionDate);
        var refreshedRange = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/plantings/{plantingId}/completion-date-range");
        Assert.Equal(shortened.ToString("yyyy-MM-dd"), refreshedRange.GetProperty("completionDate").GetString());

        using var material = await owner.PostAsJsonAsync("/api/v1/fertilizers", new { name = "有機肥", stockUnit = "公斤" });
        var materialId = (await material.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var lot = await owner.PostAsJsonAsync($"/api/v1/fertilizers/{materialId}/lots", new
        {
            source = "openingStock",
            receivedQuantity = 5m,
            availableFrom = today.AddDays(-30)
        });
        var lotId = (await lot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var afterNewCompletion = shortened.AddDays(1);
        var rejectedBackfills = new (string Url, object Body, string Field)[]
        {
            ($"/api/v1/plantings/{plantingId}/harvests",
                new { harvestDate = afterNewCompletion, weightKg = 2m }, "harvestDate"),
            ($"/api/v1/plantings/{plantingId}/irrigations",
                new { operationDate = afterNewCompletion, method = "drip" }, "operationDate"),
            ($"/api/v1/plantings/{plantingId}/material-applications", new
            {
                operationDate = afterNewCompletion,
                applicationMethod = "broadcast",
                details = new[] { new { materialPurchaseLotId = lotId, quantity = 1m } }
            }, "operationDate")
        };
        foreach (var (url, body, field) in rejectedBackfills)
        {
            using var rejected = await owner.PostAsJsonAsync(url, body);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
        }

        using var allowedOnEnd = await owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/irrigations",
            new { operationDate = shortened, method = "drip" });
        Assert.Equal(HttpStatusCode.Created, allowedOnEnd.StatusCode);

        using var extended = await owner.PutAsJsonAsync(editCompletionUrl, new { completionDate = today });
        Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
        Assert.Equal(today, Assert.Single((await owner.GetFromJsonAsync<PlantingHistoryItemDto[]>(HistoryUrl))!).CompletionDate);
    }

    /// <summary>驗證修正結束日端點的輸入格式、上限、下限衝突、狀態限制及帳號權限隔離。</summary>
    [Fact]
    public async Task CompletionDateEndpointsEnforceValidationBoundsStatusAndOwnership()
    {
        using var anonymous = fixture.CreateClient();
        using var admin = (await fixture.CreateAccountClientAsync(UserRole.Admin)).Client;
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        using var other = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var planted = today.AddDays(-20);
        var initialCompletion = today.AddDays(-5);
        var completedId = await CreateCompletedPlantingAsync(owner, fieldId, planted, initialCompletion);
        var rangeUrl = $"/api/v1/plantings/{completedId}/completion-date-range";
        var editUrl = $"/api/v1/plantings/{completedId}/completion-date";

        var invalidPayloads = new[]
        {
            "{}",
            """{"completionDate":null}""",
            """{"completionDate":""}""",
            """{"completionDate":"2026-02-30"}""",
            $$$"""{"completionDate":"{{{today.AddDays(1):yyyy-MM-dd}}}"}"""
        };
        foreach (var json in invalidPayloads)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var invalid = await owner.PutAsync(editUrl, content);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
            var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.GetProperty("errors").TryGetProperty("completionDate", out var messages), problem.GetRawText());
            if (json.Contains(today.AddDays(1).ToString("yyyy-MM-dd"), StringComparison.Ordinal))
            {
                Assert.Equal("結束日不得晚於今天。", messages[0].GetString());
            }
        }

        // 無活動紀錄時早於實際栽種日回傳 409，並帶入實際栽種日。
        using var beforePlanting = await owner.PutAsJsonAsync(editUrl, new { completionDate = planted.AddDays(-1) });
        Assert.Equal(HttpStatusCode.Conflict, beforePlanting.StatusCode);
        var beforePlantingProblem = await beforePlanting.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(planted.ToString("yyyy-MM-dd"),
            beforePlantingProblem.GetProperty("errors").GetProperty("completionDate")[0].GetString());

        // 有較晚灌溉紀錄時早於最後活動日回傳 409，並帶入最後活動日。
        var irrigationDate = today.AddDays(-10);
        using var irrigation = await owner.PostAsJsonAsync($"/api/v1/plantings/{completedId}/irrigations",
            new { operationDate = irrigationDate, method = "drip" });
        Assert.Equal(HttpStatusCode.Created, irrigation.StatusCode);
        using var beforeActivity = await owner.PutAsJsonAsync(editUrl, new { completionDate = irrigationDate.AddDays(-1) });
        Assert.Equal(HttpStatusCode.Conflict, beforeActivity.StatusCode);
        var beforeActivityProblem = await beforeActivity.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(irrigationDate.ToString("yyyy-MM-dd"),
            beforeActivityProblem.GetProperty("errors").GetProperty("completionDate")[0].GetString());

        // 已結束的栽培仍不得透過整筆編輯端點修改。
        using var regularEdit = await owner.PutAsJsonAsync($"/api/v1/plantings/{completedId}", new
        {
            cropName = "番茄",
            areaSquareMeters = 25,
            status = "completed",
            actualPlantingDate = planted,
            completionDate = today.AddDays(-4)
        });
        Assert.Equal(HttpStatusCode.Conflict, regularEdit.StatusCode);
        Assert.True((await regularEdit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors").TryGetProperty("status", out _));

        // 未結束的三種狀態均無法查詢或修正結束日。
        var nonCompletedBodies = new object[]
        {
            new { cropName = "計畫番茄", areaSquareMeters = 10, status = "planned" },
            new { cropName = "生長番茄", areaSquareMeters = 10, status = "growing", actualPlantingDate = planted },
            new { cropName = "採收番茄", areaSquareMeters = 10, status = "harvesting", actualPlantingDate = planted }
        };
        foreach (var body in nonCompletedBodies)
        {
            using var created = await owner.PostAsJsonAsync($"{FieldsUrl}/{fieldId}/plantings", body);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            using var getRange = await owner.GetAsync($"/api/v1/plantings/{id}/completion-date-range");
            Assert.Equal(HttpStatusCode.Conflict, getRange.StatusCode);
            Assert.True((await getRange.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("errors").TryGetProperty("status", out _));

            using var putDate = await owner.PutAsJsonAsync($"/api/v1/plantings/{id}/completion-date",
                new { completionDate = today });
            Assert.Equal(HttpStatusCode.Conflict, putDate.StatusCode);
            Assert.True((await putDate.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("errors").TryGetProperty("status", out _));
        }

        // 驗證未登入、缺少 USER 角色、跨帳號與不存在識別碼的回應。
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(rangeUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync(editUrl, new { completionDate = today })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(rangeUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PutAsJsonAsync(editUrl, new { completionDate = today })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(rangeUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync(editUrl, new { completionDate = today })).StatusCode);

        var missingId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/v1/plantings/{missingId}/completion-date-range")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PutAsJsonAsync($"/api/v1/plantings/{missingId}/completion-date", new { completionDate = today })).StatusCode);

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/plantings/{completedId}");
        Assert.Equal(initialCompletion.ToString("yyyy-MM-dd"), detail.GetProperty("completionDate").GetString());
    }

    /// <summary>並行提前結束日與補登灌溉時，兩者不得同時成功而使灌溉日晚於結束日。</summary>
    [Fact]
    public async Task ConcurrentCompletionDateEditAndIrrigationBackfillStayConsistent()
    {
        using var owner = (await fixture.CreateAccountClientAsync()).Client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var plantingId = await CreateCompletedPlantingAsync(
            owner, fieldId, today.AddDays(-20), today.AddDays(-2));
        var shortenedDate = today.AddDays(-10);
        var backfillDate = today.AddDays(-5);

        var editTask = owner.PutAsJsonAsync($"/api/v1/plantings/{plantingId}/completion-date",
            new { completionDate = shortenedDate });
        var irrigateTask = owner.PostAsJsonAsync($"/api/v1/plantings/{plantingId}/irrigations",
            new { operationDate = backfillDate, method = "drip" });
        await Task.WhenAll(editTask, irrigateTask);
        using var edited = await editTask;
        using var irrigated = await irrigateTask;

        Assert.True((edited.StatusCode, irrigated.StatusCode) is
            (HttpStatusCode.OK, HttpStatusCode.BadRequest) or (HttpStatusCode.Conflict, HttpStatusCode.Created),
            $"Unexpected statuses: edit={edited.StatusCode}, irrigate={irrigated.StatusCode}");

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/plantings/{plantingId}");
        var irrigations = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/plantings/{plantingId}/irrigations"))
            .EnumerateArray().ToList();
        if (edited.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(shortenedDate.ToString("yyyy-MM-dd"), detail.GetProperty("completionDate").GetString());
            Assert.Empty(irrigations);
        }
        else
        {
            Assert.Equal(today.AddDays(-2).ToString("yyyy-MM-dd"), detail.GetProperty("completionDate").GetString());
            var createdIrrigation = Assert.Single(irrigations);
            Assert.Equal(backfillDate.ToString("yyyy-MM-dd"), createdIrrigation.GetProperty("operationDate").GetString());
        }
    }

    /// <summary>透過田區與栽培 API 建立測試用已結束栽培紀錄。</summary>
    private static async Task<Guid> CreateCompletedPlantingAsync(HttpClient owner, Guid fieldId,
        DateOnly actualPlantingDate, DateOnly completionDate, DateOnly? plannedPlantingDate = null,
        string cropName = "番茄", string? variety = null, string? notes = null)
    {
        using var created = await owner.PostAsJsonAsync($"{FieldsUrl}/{fieldId}/plantings", new
        {
            cropName,
            variety,
            areaSquareMeters = 25,
            status = "growing",
            plannedPlantingDate,
            actualPlantingDate,
            notes
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plantingUrl = created.Headers.Location!;
        var plantingId = Guid.Parse(plantingUrl.AbsolutePath.Split('/')[^1]);

        using var completed = await owner.PutAsJsonAsync(plantingUrl, new
        {
            cropName,
            variety,
            areaSquareMeters = 25,
            status = "completed",
            plannedPlantingDate,
            actualPlantingDate,
            completionDate,
            notes
        });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        return plantingId;
    }

    /// <summary>透過田區 API 建立測試用田區。</summary>
    private static async Task<Guid> CreateFieldAsync(HttpClient owner, string name)
    {
        using var response = await owner.PostAsJsonAsync(FieldsUrl, new { name, areaSquareMeters = 100 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<FieldRsDto>())!.Fields).Id;
    }
}
