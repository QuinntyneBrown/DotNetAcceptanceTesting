using FrontendGateway.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Messaging;
using Shared.Messages.Commands;
using Shared.Messages.Queries;

namespace FrontendGateway.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MissionsController : ControllerBase
{
    private readonly IMessagePublisher _publisher;
    private readonly IMessageSubscriber _subscriber;
    private readonly ILogger<MissionsController> _logger;

    public MissionsController(
        IMessagePublisher publisher,
        IMessageSubscriber subscriber,
        ILogger<MissionsController> logger)
    {
        _publisher = publisher;
        _subscriber = subscriber;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetMissions()
    {
        var correlationId = Guid.NewGuid();
        var responseReceived = new TaskCompletionSource<GetMissionListQueryResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var responseChannel = $"{Channels.GetMissionListQueryResponse}.{correlationId}";

        await _subscriber.SubscribeAsync<GetMissionListQueryResponse>(responseChannel, response =>
        {
            responseReceived.TrySetResult(response);
            return Task.CompletedTask;
        });

        try
        {
            var query = new GetMissionListQuery { CorrelationId = correlationId };
            _logger.LogInformation("Dispatching GetMissionListQuery {CorrelationId}", correlationId);

            await _publisher.PublishAsync(Channels.GetMissionListQuery, query);

            var response = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return Ok(response);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for GetMissionListQueryResponse {CorrelationId}", correlationId);
            return StatusCode(504, "Downstream service did not respond in time.");
        }
        finally
        {
            await _subscriber.UnsubscribeAsync(responseChannel);
        }
    }

    [HttpGet("{missionId:guid}")]
    public async Task<IActionResult> GetMission(Guid missionId)
    {
        var correlationId = Guid.NewGuid();
        var responseReceived = new TaskCompletionSource<GetMissionQueryResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var responseChannel = $"{Channels.GetMissionQueryResponse}.{correlationId}";

        await _subscriber.SubscribeAsync<GetMissionQueryResponse>(responseChannel, response =>
        {
            responseReceived.TrySetResult(response);
            return Task.CompletedTask;
        });

        try
        {
            var query = new GetMissionQuery
            {
                CorrelationId = correlationId,
                MissionId = missionId
            };
            _logger.LogInformation("Dispatching GetMissionQuery {CorrelationId} for {MissionId}",
                correlationId, missionId);

            await _publisher.PublishAsync(Channels.GetMissionQuery, query);

            var response = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

            if (!response.Success)
                return NotFound(response.ErrorMessage);

            return Ok(response);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for GetMissionQueryResponse {CorrelationId}", correlationId);
            return StatusCode(504, "Downstream service did not respond in time.");
        }
        finally
        {
            await _subscriber.UnsubscribeAsync(responseChannel);
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateMission([FromBody] CreateMissionRequest request)
    {
        var command = new CreateMissionCommand
        {
            CorrelationId = Guid.NewGuid(),
            MissionName = request.MissionName,
            LaunchSite = request.LaunchSite,
            ScheduledLaunch = request.ScheduledLaunch,
            PayloadDescription = request.PayloadDescription
        };

        _logger.LogInformation("Publishing CreateMissionCommand {CorrelationId} for {MissionName}",
            command.CorrelationId, command.MissionName);

        await _publisher.PublishAsync(Channels.CreateMissionCommand, command);

        return Accepted(new { command.CorrelationId });
    }

    [HttpPut("{missionId:guid}")]
    public async Task<IActionResult> UpdateMission(Guid missionId, [FromBody] UpdateMissionRequest request)
    {
        var command = new UpdateMissionCommand
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = missionId,
            MissionName = request.MissionName,
            LaunchSite = request.LaunchSite,
            ScheduledLaunch = request.ScheduledLaunch,
            PayloadDescription = request.PayloadDescription
        };

        _logger.LogInformation("Publishing UpdateMissionCommand {CorrelationId} for {MissionId}",
            command.CorrelationId, missionId);

        await _publisher.PublishAsync(Channels.UpdateMissionCommand, command);

        return Accepted(new { command.CorrelationId });
    }

    [HttpDelete("{missionId:guid}")]
    public async Task<IActionResult> DeleteMission(Guid missionId)
    {
        var command = new DeleteMissionCommand
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = missionId
        };

        _logger.LogInformation("Publishing DeleteMissionCommand {CorrelationId} for {MissionId}",
            command.CorrelationId, missionId);

        await _publisher.PublishAsync(Channels.DeleteMissionCommand, command);

        return Accepted(new { command.CorrelationId });
    }
}
