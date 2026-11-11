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

    /// <summary>驗證三種初始狀態、可空日期及新增後的讀取路徑。</summary>
    [Theory]
    [InlineData("""{"cropName":" 番茄 "}""", "planned", null)]
    [InlineData("""{"cropName":" 番茄 ","status":"harvesting","actualPlantingDate":null}""", "harvesting", null)]
    [InlineData("""{"cropName":" 番茄 ","status":"growing","actualPlantingDate":"2026-11-01"}""", "growing", "2026-11-01")]
    public async Task CreatesPlantingAndReadsItFromLocationListAndOverview(string json, string status, string? date)
    {
        var (client, _) = await fixture.CreateAccountClientAsync();
        using var owner = client;
        var fieldId = await CreateFieldAsync(owner, "北側田");
        var url = $"{FieldsUrl}/{fieldId}/plantings";
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());
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
        Assert.Equal($"{url}/{plantingId}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(plantingId, (await owner.GetFromJsonAsync<JsonElement>(response.Headers.Location)).GetProperty("id").GetGuid());
        Assert.Equal(plantingId, Assert.Single((await owner.GetFromJsonAsync<JsonElement>(url)).EnumerateArray()).GetProperty("id").GetGuid());
        var overview = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(OverviewUrl)).EnumerateArray());
        Assert.Equal(fieldId, overview.GetProperty("id").GetGuid());
        Assert.Equal(plantingId, Assert.Single(overview.GetProperty("plantings").EnumerateArray()).GetProperty("id").GetGuid());
    }

    /// <summary>驗證無效的狀態或日期回傳欄位錯誤且沒有新增資料。</summary>
    [Theory]
    [InlineData("""{"cropName":"番茄","status":"growing"}""", "actualPlantingDate")]
    [InlineData("""{"cropName":"番茄","status":"growing","actualPlantingDate":null}""", "actualPlantingDate")]
    [InlineData("""{"cropName":"番茄","status":"completed"}""", "status")]
    [InlineData("""{"cropName":"番茄","status":3}""", "status")]
    [InlineData("""{"cropName":"番茄","status":null}""", "status")]
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
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());
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
        var secondField = await CreateFieldAsync(owner, "南側田");
        var url = $"{FieldsUrl}/{fieldId}/plantings";
        var request = new { cropName = "番茄" };
        using var created = await owner.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plantingId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(OverviewUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(created.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{FieldsUrl}/{secondField}/plantings/{plantingId}")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>(OverviewUrl)).EnumerateArray());
        Assert.Single((await owner.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());
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
            var detail = await owner.GetFromJsonAsync<JsonElement>($"{fieldUrl}/plantings/{completedId}");
            Assert.Equal("completed", detail.GetProperty("status").GetString());
            Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"{fieldUrl}/plantings")).EnumerateArray());
            var overview = await owner.GetFromJsonAsync<JsonElement>(OverviewUrl);
            var field = Assert.Single(overview.EnumerateArray(), entry => entry.GetProperty("id").GetGuid() == protectedField);
            Assert.Empty(field.GetProperty("plantings").EnumerateArray());
        }
        else
        {
            using var created = await owner.PostAsJsonAsync($"{fieldUrl}/plantings", new { cropName = "番茄" });
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
        var createTask = owner.PostAsJsonAsync($"{fieldUrl}/plantings", new { cropName = "番茄" });
        var deleteTask = owner.DeleteAsync(fieldUrl);
        await Task.WhenAll(createTask, deleteTask);
        using var created = await createTask;
        using var deleted = await deleteTask;

        if (created.StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
            Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"{fieldUrl}/plantings")).EnumerateArray());
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(OverviewUrl)).EnumerateArray());
        }
    }

    /// <summary>透過田區 API 建立測試用田區。</summary>
    private static async Task<Guid> CreateFieldAsync(HttpClient owner, string name)
    {
        using var response = await owner.PostAsJsonAsync(FieldsUrl, new { name, areaSquareMeters = 100 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<FieldRsDto>())!.Fields).Id;
    }
}
