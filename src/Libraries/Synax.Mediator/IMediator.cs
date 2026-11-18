namespace Synax.Mediator;

/// <summary>
/// Unified interface for sending requests and publishing notifications.
/// </summary>
public interface IMediator : ISender, IPublisher;
