using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Application.Contracts.Cuisines;
using Synax.Crud;
using Synax.EntityFrameworkCore.Abstractions.Patching;

namespace Synax.BookingDemoApp.Application.Features.Cuisines;

// Generic CRUD one-liners: no handlers, no mapper profiles, no endpoints logic — the
// Synax.Crud handlers registered by AddSynaxCrud serve these through the full mediator
// pipeline (timing behavior, result logging, ownership query filters). Operations with real
// business rules stay handwritten in this folder (CreateCuisine's tenant-scoped duplicate
// check, GetCuisines' name-ordered unpaged list) — a handwritten handler for one of these
// requests would automatically win over the generic one.

/// <summary>GET /api/v1/cuisines/{id} — 404 (CUISINE_NOT_FOUND) or the projected response.</summary>
public sealed class GetCuisineRequest : CrudGetRequest<Cuisine, Guid, CuisineResponse>;

/// <summary>DELETE /api/v1/cuisines/{id} — hard delete (Cuisine is not soft-deletable).</summary>
public sealed class DeleteCuisineRequest : CrudDeleteRequest<Cuisine, Guid>;

/// <summary>
/// PATCH /api/v1/cuisines/{id} — merge-patch: only members present in the body are written
/// (single set-based UPDATE); an explicit <c>"description": null</c> clears the description.
/// </summary>
public sealed class PatchCuisineRequest : CrudPatchRequest<Cuisine, Guid, CuisineResponse>
{
    public PatchValue<string> Name { get; set; }
    public PatchValue<string?> Description { get; set; }
}
