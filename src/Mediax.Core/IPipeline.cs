namespace Mediax.Core;

/// <summary>Marker interface for a composed pipeline of behaviors for a specific request/response pair.</summary>
public interface IPipeline<TReq, TRes> where TReq : IRequest<TRes> { }

/// <summary>Non-generic pipeline marker (used in builder APIs).</summary>
public interface IPipeline { }
