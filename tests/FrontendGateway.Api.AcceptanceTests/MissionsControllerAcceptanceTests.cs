using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FrontendGateway.Api.Contracts;
using NUnit.Framework;
using Shared;
using Shared.Messages.Commands;
using Shared.Messages.Queries;

namespace FrontendGateway.Api.AcceptanceTests;

public class MissionsControllerAcceptanceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Test]
    public async Task Post_CreateMission_publishes_CreateMissionCommand_and_returns_Accepted()
    {
        using var factory = new FrontendGatewayApiFactory();
        using var client = factory.CreateClient();

        // Arrange — subscribe to the command channel before sending the request
        var commandReceived = new TaskCompletionSource<CreateMissionCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await factory.PubSub.SubscribeAsync<CreateMissionCommand>(Channels.CreateMissionCommand, command =>
        {
            commandReceived.TrySetResult(command);
            return Task.CompletedTask;
        });

        var request = new CreateMissionRequest
        {
            MissionName = "Artemis IV",
            LaunchSite = "KSC LC-39B",
            ScheduledLaunch = new DateTime(2027, 3, 15, 14, 0, 0, DateTimeKind.Utc),
            PayloadDescription = "Lunar Gateway resupply module"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/missions", request);

        // Assert — HTTP response
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        // Assert — command published to PubSub with correct mapping
        var published = await commandReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(published.CorrelationId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(published.MissionName, Is.EqualTo("Artemis IV"));
        Assert.That(published.LaunchSite, Is.EqualTo("KSC LC-39B"));
        Assert.That(published.ScheduledLaunch, Is.EqualTo(new DateTime(2027, 3, 15, 14, 0, 0, DateTimeKind.Utc)));
        Assert.That(published.PayloadDescription, Is.EqualTo("Lunar Gateway resupply module"));
    }

    [Test]
    public async Task Put_UpdateMission_publishes_UpdateMissionCommand_and_returns_Accepted()
    {
        using var factory = new FrontendGatewayApiFactory();
        using var client = factory.CreateClient();

        var commandReceived = new TaskCompletionSource<UpdateMissionCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await factory.PubSub.SubscribeAsync<UpdateMissionCommand>(Channels.UpdateMissionCommand, command =>
        {
            commandReceived.TrySetResult(command);
            return Task.CompletedTask;
        });

        var missionId = Guid.NewGuid();
        var request = new UpdateMissionRequest
        {
            MissionName = "Artemis IV - Revised",
            LaunchSite = "KSC LC-39A",
            ScheduledLaunch = new DateTime(2027, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            PayloadDescription = "Updated payload manifest"
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/missions/{missionId}", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var published = await commandReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(published.MissionId, Is.EqualTo(missionId));
        Assert.That(published.MissionName, Is.EqualTo("Artemis IV - Revised"));
        Assert.That(published.LaunchSite, Is.EqualTo("KSC LC-39A"));
    }

    [Test]
    public async Task Delete_Mission_publishes_DeleteMissionCommand_and_returns_Accepted()
    {
        using var factory = new FrontendGatewayApiFactory();
        using var client = factory.CreateClient();

        var commandReceived = new TaskCompletionSource<DeleteMissionCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await factory.PubSub.SubscribeAsync<DeleteMissionCommand>(Channels.DeleteMissionCommand, command =>
        {
            commandReceived.TrySetResult(command);
            return Task.CompletedTask;
        });

        var missionId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/missions/{missionId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var published = await commandReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(published.MissionId, Is.EqualTo(missionId));
        Assert.That(published.CorrelationId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task Get_MissionList_publishes_query_and_returns_response_from_downstream()
    {
        using var factory = new FrontendGatewayApiFactory();
        using var client = factory.CreateClient();

        // Arrange — simulate a downstream service responding to the query
        await factory.PubSub.SubscribeAsync<GetMissionListQuery>(Channels.GetMissionListQuery, async query =>
        {
            var responseChannel = $"{Channels.GetMissionListQueryResponse}.{query.CorrelationId}";

            await factory.PubSub.PublishAsync(responseChannel, new GetMissionListQueryResponse
            {
                CorrelationId = query.CorrelationId,
                Success = true,
                Missions =
                [
                    new MissionListItem
                    {
                        MissionId = Guid.NewGuid(),
                        MissionName = "Artemis IV",
                        LaunchSite = "KSC LC-39B",
                        ScheduledLaunch = new DateTime(2027, 3, 15, 14, 0, 0, DateTimeKind.Utc)
                    },
                    new MissionListItem
                    {
                        MissionId = Guid.NewGuid(),
                        MissionName = "NROL-87",
                        LaunchSite = "Vandenberg SLC-4E",
                        ScheduledLaunch = new DateTime(2027, 5, 20, 8, 30, 0, DateTimeKind.Utc)
                    }
                ]
            });
        });

        // Act
        var response = await client.GetAsync("/api/missions");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<GetMissionListQueryResponse>(JsonOptions);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Success, Is.True);
        Assert.That(result.Missions, Has.Count.EqualTo(2));
        Assert.That(result.Missions, Has.Some.Matches<MissionListItem>(m => m.MissionName == "Artemis IV"));
        Assert.That(result.Missions, Has.Some.Matches<MissionListItem>(m => m.MissionName == "NROL-87"));
    }

    [Test]
    public async Task Get_MissionById_publishes_query_and_returns_response_from_downstream()
    {
        using var factory = new FrontendGatewayApiFactory();
        using var client = factory.CreateClient();

        var missionId = Guid.NewGuid();

        // Arrange — simulate downstream service
        await factory.PubSub.SubscribeAsync<GetMissionQuery>(Channels.GetMissionQuery, async query =>
        {
            var responseChannel = $"{Channels.GetMissionQueryResponse}.{query.CorrelationId}";

            await factory.PubSub.PublishAsync(responseChannel, new GetMissionQueryResponse
            {
                CorrelationId = query.CorrelationId,
                Success = true,
                Mission = new MissionData
                {
                    MissionId = query.MissionId,
                    MissionName = "Artemis IV",
                    LaunchSite = "KSC LC-39B",
                    ScheduledLaunch = new DateTime(2027, 3, 15, 14, 0, 0, DateTimeKind.Utc),
                    PayloadDescription = "Lunar Gateway resupply module"
                }
            });
        });

        // Act
        var response = await client.GetAsync($"/api/missions/{missionId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<GetMissionQueryResponse>(JsonOptions);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Success, Is.True);
        Assert.That(result.Mission!.MissionId, Is.EqualTo(missionId));
        Assert.That(result.Mission.MissionName, Is.EqualTo("Artemis IV"));
    }
}
