using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsService.Api.Contracts;
using SmsService.Tests.Infrastructure;

namespace SmsService.Tests;

[Collection(SqlServerCollection.Name)]
public class MessagesApiTests(SqlServerFixture sql)
{
    private static HttpRequestMessage Post(object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/messages") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    [Fact]
    public async Task Same_idempotency_key_twice_returns_same_id_and_stores_one_message()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();
        var body = new { to = "050-1234567", body = "שלום, הישיבה נדחתה למחר" };

        var first = await client.SendAsync(Post(body, "key-1"));
        var second = await client.SendAsync(Post(body, "key-1"));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        first.Headers.Location!.ToString().Should().StartWith("/messages/");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstId = (await first.Content.ReadFromJsonAsync<SendMessageResponse>())!.Id;
        var secondId = (await second.Content.ReadFromJsonAsync<SendMessageResponse>())!.Id;
        secondId.Should().Be(firstId);

        await using var db = SqlServerFixture.CreateContext(cs);
        (await db.Messages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Same_idempotency_key_with_different_body_returns_409()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        await client.SendAsync(Post(new { to = "0501234567", body = "א" }, "key-2"));
        var conflict = await client.SendAsync(Post(new { to = "0501234567", body = "ב" }, "key-2"));

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_input_returns_400_with_all_errors()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        var response = await client.SendAsync(Post(new { to = "02-6543210", body = "   " }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Keys.Should().BeEquivalentTo(["to", "body"]);
    }

    [Fact]
    public async Task Get_by_id_returns_status_only_and_handles_404_and_400()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        var created = await client.SendAsync(Post(new { to = "050-1234567", body = "hello" }));
        var id = (await created.Content.ReadFromJsonAsync<SendMessageResponse>())!.Id;

        var response = await client.GetAsync($"/messages/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        status!.Id.Should().Be(id);
        status.Status.Should().BeOneOf("Pending", "Sending", "Sent", "Failed");

        // The recipient and the body are not part of the status response.
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("\"to\"").And.NotContain("\"body\"").And.NotContain("\"employees\"");

        (await client.GetAsync($"/messages/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/messages/not-a-guid")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_returns_full_details_with_owning_employees()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs); // startup seeds employees from employees.seed.json
        var client = factory.CreateClient();

        // In the seed file, 053-3333333 is shared by two employees.
        var created = await client.SendAsync(Post(new { to = "053-3333333", body = "hello" }));
        var id = (await created.Content.ReadFromJsonAsync<SendMessageResponse>())!.Id;

        var list = await client.GetFromJsonAsync<MessageListResponse>("/messages");
        var message = list!.Items.Single(m => m.Id == id);
        message.To.Should().Be("+972533333333");
        message.Segments.Should().Be(1);
        message.Employees.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_filters_by_status_case_insensitively_and_rejects_unknown_status()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();
        await client.SendAsync(Post(new { to = "0501234567", body = "one" }));

        var pending = await client.GetFromJsonAsync<MessageListResponse>("/messages?status=PENDING");
        var sent = await client.GetFromJsonAsync<MessageListResponse>("/messages?status=sent");
        var all = await client.GetFromJsonAsync<MessageListResponse>("/messages");

        all!.Items.Should().ContainSingle();
        pending!.Items.Should().ContainSingle().Which.Status.Should().Be("Pending");
        sent!.Items.Should().BeEmpty();
        (await client.GetAsync("/messages?status=Bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/messages?status=1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
