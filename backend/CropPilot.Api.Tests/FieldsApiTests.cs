using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;
using Xunit;

namespace CropPilot.Api.Tests;

/// <summary>以實際 HTTP 路由、Bearer 驗證及 PostgreSQL 檢查田區 API。</summary>
public sealed class FieldsApiTests(FieldsApiFixture fixture) : IClassFixture<FieldsApiFixture>
{
    private const string FieldsUrl = "/api/v1/fields";

    /// <summary>驗證只填必填欄位即可建立田區，且回傳的查詢位置可讀到已存資料。</summary>
    [Fact]
    public async Task CreatesFieldWithOnlyRequiredValuesAndReturnsItsLocation()
    {
        var (client, accountId) = await fixture.CreateAccountClientAsync();
        using var owner = client;
        using var response = await owner.PostAsJsonAsync(FieldsUrl, new { name = " 北側田 ", areaSquareMeters = 1200.5m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.Single((await response.Content.ReadFromJsonAsync<FieldRsDto>())!.Fields);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("北側田", created.Name);
        Assert.Equal(1200.5m, created.AreaSquareMeters);
        Assert.Null(created.Latitude);
        Assert.Null(created.Longitude);
        Assert.Null(created.Notes);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal($"{FieldsUrl}/{created.Id}", location.AbsolutePath);
        Assert.Equal(created, await owner.GetFromJsonAsync<FieldDto>(location));
        Assert.Equal(created, Assert.Single((await owner.GetFromJsonAsync<FieldRsDto>(FieldsUrl))!.Fields));
        Assert.Equal(1, await fixture.CountFieldsAsync(accountId));
    }

    /// <summary>驗證無效輸入會回傳對應欄位的 HTTP 400，且資料庫維持零筆。</summary>
    [Theory]
    [InlineData("""{"areaSquareMeters":1}""", "name")]
    [InlineData("""{"name":"  ","areaSquareMeters":1}""", "name")]
    [InlineData("""{"name":"北側田"}""", "areaSquareMeters")]
    [InlineData("""{"name":"北側田","areaSquareMeters":0}""", "areaSquareMeters")]
    [InlineData("""{"name":"北側田","areaSquareMeters":"1"}""", "areaSquareMeters")]
    [InlineData("""{"name":"北側田","areaSquareMeters":10000000000}""", "areaSquareMeters")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1.00001}""", "areaSquareMeters")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1.000000000000000000000000000001}""", "areaSquareMeters")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1,"latitude":23}""", "longitude")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1,"longitude":121}""", "latitude")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1,"latitude":23.0000001,"longitude":121}""", "latitude")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1,"latitude":90.000000000000000000000000000001,"longitude":121}""", "latitude")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1,"latitude":23,"longitude":181}""", "longitude")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1,"latitude":"23","longitude":121}""", "latitude")]
    [InlineData("""{"name":"北\u0000側田","areaSquareMeters":1}""", "name")]
    [InlineData("""{"name":"北側田","areaSquareMeters":1,"notes":"\u0000"}""", "notes")]
    public async Task RejectsInvalidFieldsWithoutSaving(string json, string errorField)
    {
        var (client, accountId) = await fixture.CreateAccountClientAsync();
        using var owner = client;
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await owner.PostAsync(FieldsUrl, body);

        await AssertFieldErrorAsync(response, HttpStatusCode.BadRequest, errorField);
        Assert.Equal(0, await fixture.CountFieldsAsync(accountId));
    }

    /// <summary>驗證並行送出同名田區時只會存入一筆，另一筆回傳 HTTP 409。</summary>
    [Fact]
    public async Task ConcurrentCreatesWithTheSameTrimmedNameSaveExactlyOneField()
    {
        var (client, accountId) = await fixture.CreateAccountClientAsync();
        using var owner = client;
        var responses = await Task.WhenAll(
            owner.PostAsJsonAsync(FieldsUrl, new { name = "北側田", areaSquareMeters = 1 }),
            owner.PostAsJsonAsync(FieldsUrl, new { name = " 北側田 ", areaSquareMeters = 2 }));
        using var first = responses[0];
        using var second = responses[1];

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertFieldErrorAsync(conflict, HttpStatusCode.Conflict, "name");
        Assert.Equal(1, await fixture.CountFieldsAsync(accountId));
    }

    /// <summary>驗證指數表示法與尾隨零可精確儲存為契約允許的數值。</summary>
    [Fact]
    public async Task AcceptsExactDecimalInputsWithExponentsAndTrailingZeros()
    {
        var (client, _) = await fixture.CreateAccountClientAsync();
        using var owner = client;
        using var body = new StringContent("""
            {"name":"北側田","areaSquareMeters":100e-6,"latitude":9e+1,"longitude":-0.00000100}
            """, Encoding.UTF8, "application/json");
        using var response = await owner.PostAsync(FieldsUrl, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var field = Assert.Single((await response.Content.ReadFromJsonAsync<FieldRsDto>())!.Fields);
        Assert.Equal(0.0001m, field.AreaSquareMeters);
        Assert.Equal(90m, field.Latitude);
        Assert.Equal(-0.000001m, field.Longitude);
    }

    /// <summary>驗證名稱與備註以 Unicode 碼點計算字數，並檢查上限前後的行為。</summary>
    [Theory]
    [InlineData(120, 1000, null)]
    [InlineData(121, 1000, "name")]
    [InlineData(120, 1001, "notes")]
    public async Task FieldTextLimitsCountUnicodeCodePoints(int nameLength, int notesLength, string? errorField)
    {
        var (client, accountId) = await fixture.CreateAccountClientAsync();
        using var owner = client;
        var name = string.Concat(Enumerable.Repeat("𠮷", nameLength));
        var notes = string.Concat(Enumerable.Repeat("🌱", notesLength));
        using var response = await owner.PostAsJsonAsync(FieldsUrl, new { name, areaSquareMeters = 1, notes });

        if (errorField is not null)
        {
            await AssertFieldErrorAsync(response, HttpStatusCode.BadRequest, errorField);
            Assert.Equal(0, await fixture.CountFieldsAsync(accountId));
            return;
        }

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var saved = await owner.GetFromJsonAsync<FieldDto>(response.Headers.Location);
        Assert.Equal(name, saved!.Name);
        Assert.Equal(notes, saved.Notes);
    }

    /// <summary>驗證跨帳號可使用相同名稱，且查詢結果只包含登入帳號的資料。</summary>
    [Fact]
    public async Task AccountsCanReuseNamesAndCannotReadEachOthersFields()
    {
        var (firstClient, firstId) = await fixture.CreateAccountClientAsync();
        using var first = firstClient;
        var (secondClient, secondId) = await fixture.CreateAccountClientAsync();
        using var second = secondClient;
        Assert.Empty((await second.GetFromJsonAsync<FieldRsDto>(FieldsUrl))!.Fields);

        var request = new { name = "North", areaSquareMeters = 1, latitude = 23.5m, longitude = 121.1m, notes = "試驗田" };
        using var firstResponse = await first.PostAsJsonAsync(FieldsUrl, request);
        using var secondResponse = await second.PostAsJsonAsync(FieldsUrl, request);
        using var caseVariant = await first.PostAsJsonAsync(FieldsUrl, new { name = "north", areaSquareMeters = 1 });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, caseVariant.StatusCode);

        using var otherField = await second.GetAsync(firstResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.NotFound, otherField.StatusCode);
        var ownField = Assert.Single((await second.GetFromJsonAsync<FieldRsDto>(FieldsUrl))!.Fields);
        Assert.Equal(secondResponse.Headers.Location!.AbsolutePath, $"{FieldsUrl}/{ownField.Id}");
        Assert.Equal(request.latitude, ownField.Latitude);
        Assert.Equal(request.longitude, ownField.Longitude);
        Assert.Equal(request.notes, ownField.Notes);
        Assert.Equal(2, await fixture.CountFieldsAsync(firstId));
        Assert.Equal(1, await fixture.CountFieldsAsync(secondId));
    }

    /// <summary>驗證三個田區端點皆要求 Bearer 權杖。</summary>
    [Fact]
    public async Task FieldEndpointsRequireBearerAuthentication()
    {
        using var anonymous = fixture.CreateClient();
        using var list = await anonymous.GetAsync(FieldsUrl);
        using var get = await anonymous.GetAsync($"{FieldsUrl}/{Guid.NewGuid()}");
        using var create = await anonymous.PostAsJsonAsync(FieldsUrl, new { name = "北側田", areaSquareMeters = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    /// <summary>驗證已簽章但 <c>sub</c> 無效的權杖在工作階段和田區端點皆回傳 HTTP 401。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("invalid-subject")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData(" 11111111-1111-1111-1111-111111111111 ")]
    [InlineData("11111111111111111111111111111111")]
    public async Task BearerEndpointsRejectInvalidSubjectsInSignedTokens(string? subject)
    {
        using var client = fixture.CreateClientWithSubject(subject);
        using var session = await client.GetAsync("/api/v1/auth/session");
        using var fields = await client.GetAsync(FieldsUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, fields.StatusCode);
    }

    /// <summary>驗證新增的田區屬於已簽章的 <c>sub</c> 所指定的主帳號。</summary>
    [Fact]
    public async Task BearerEndpointsUseTheSignedSubjectAsTheMainAccountId()
    {
        var (owner, accountId) = await fixture.CreateAccountClientAsync();
        using var originalClient = owner;
        using var client = fixture.CreateClientWithSubject(accountId.ToString("D"));
        using var session = await client.GetAsync("/api/v1/auth/session");
        using var create = await client.PostAsJsonAsync(FieldsUrl, new { name = "北側田", areaSquareMeters = 1 });

        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(1, await fixture.CountFieldsAsync(accountId));
    }

    /// <summary>驗證缺少 <c>USER</c> 角色的已驗證帳號會收到 HTTP 403。</summary>
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(null)]
    public async Task FieldEndpointsRequireTheUserRole(string? role)
    {
        var (client, accountId) = await fixture.CreateAccountClientAsync(role);
        using var account = client;
        using var list = await account.GetAsync(FieldsUrl);
        using var get = await account.GetAsync($"{FieldsUrl}/{Guid.NewGuid()}");
        using var create = await account.PostAsJsonAsync(FieldsUrl, new { name = "北側田", areaSquareMeters = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(0, await fixture.CountFieldsAsync(accountId));
    }

    /// <summary>核對欄位錯誤的 HTTP 狀態、媒體類型及回應欄位。</summary>
    private static async Task AssertFieldErrorAsync(HttpResponseMessage response, HttpStatusCode status, string field)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.NotEmpty(problem.GetProperty("errors").GetProperty(field).EnumerateArray());
    }
}
