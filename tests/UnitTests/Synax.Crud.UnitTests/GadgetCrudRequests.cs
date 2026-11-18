using Synax.AspNetCore.Errors;
using Synax.EntityFrameworkCore.Abstractions.Patching;
using Synax.Mediator;
using Synax.Results;

namespace Synax.Crud.UnitTests;

public sealed class GetGadgetRequest : CrudGetRequest<Gadget, Guid, GadgetResponse>;

public sealed class ListGadgetsRequest : CrudListRequest<Gadget, GadgetResponse>;

public sealed class CreateGadgetRequest : CrudCreateRequest<Gadget, GadgetResponse>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Stock { get; set; }
}

public sealed class PatchGadgetRequest : CrudPatchRequest<Gadget, Guid, GadgetResponse>
{
    public PatchValue<string> Name { get; set; }
    public PatchValue<int> Stock { get; set; }
}

public sealed class DeleteGadgetRequest : CrudDeleteRequest<Gadget, Guid>;

/// <summary>Used to prove a handwritten handler wins over the generic one for the same request type.</summary>
public sealed class OverriddenGetGadgetRequest : CrudGetRequest<Gadget, Guid, GadgetResponse>;

public sealed class OverriddenGetGadgetHandler : IQueryHandler<OverriddenGetGadgetRequest, Result<GadgetResponse, HttpError>>
{
    public Task<Result<GadgetResponse, HttpError>> HandleAsync(OverriddenGetGadgetRequest request, CancellationToken cancellationToken)
        => Task.FromResult<Result<GadgetResponse, HttpError>>(new GadgetResponse { Id = request.Id, Name = "handwritten" });
}

// --------- Negative-case types: only used via the explicit AddSynaxCrud(params Type[]) overload. ---------

public sealed class BadCreateGadgetRequest : CrudCreateRequest<Gadget, GadgetResponse>
{
    public string NoSuchProperty { get; set; } = string.Empty;
}

public sealed class EmptyPatchGadgetRequest : CrudPatchRequest<Gadget, Guid, GadgetResponse>;

public sealed class WrongKeyGetGadgetRequest : CrudGetRequest<Gadget, int, GadgetResponse>;
