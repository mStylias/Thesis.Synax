using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Patching;
using Synax.EntityFrameworkCore.Testing;
using Synax.Mediator;

namespace Synax.Crud.UnitTests;

/// <summary>
/// Dispatches the Gadget CRUD requests through <see cref="ISender"/> against a
/// <see cref="Synax.EntityFrameworkCore.Testing.FakeUnitOfWork{TestDbContext}"/> — exercises the
/// generic handlers exactly as the app would, through the full mediator pipeline.
/// </summary>
public class GadgetCrudTests
{
    [Test]
    public async Task Get_ReturnsProjectedResponse_ForSeededEntity()
    {
        var (sender, uow, _) = CrudTestHarness.Create();
        var gadget = new Gadget { Name = "Widget", Description = "A gadget", Stock = 5 };
        uow.Seed(gadget);

        var result = await sender.SendAsync(new GetGadgetRequest { Id = gadget.Id });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Id).IsEqualTo(gadget.Id);
        await Assert.That(result.Data!.Name).IsEqualTo("Widget");
        await Assert.That(result.Data!.Description).IsEqualTo("A gadget");
        await Assert.That(result.Data!.Stock).IsEqualTo(5);
    }

    [Test]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var (sender, _, _) = CrudTestHarness.Create();

        var result = await sender.SendAsync(new GetGadgetRequest { Id = Guid.NewGuid() });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo("GADGET_NOT_FOUND");
    }

    [Test]
    public async Task List_ReturnsPagedResults_OrderedByKey()
    {
        var (sender, uow, _) = CrudTestHarness.Create();
        var ids = Enumerable.Range(0, 3)
            .Select(_ => Guid.CreateVersion7())
            .OrderBy(id => id)
            .ToArray();

        uow.Seed(
            new Gadget { Id = ids[0], Name = "A" },
            new Gadget { Id = ids[1], Name = "B" },
            new Gadget { Id = ids[2], Name = "C" });

        var result = await sender.SendAsync(new ListGadgetsRequest { Page = 1, PageSize = 2 });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Items.Count).IsEqualTo(2);
        await Assert.That(result.Data!.TotalCount).IsEqualTo(3);
        await Assert.That(result.Data!.Items[0].Id).IsEqualTo(ids[0]);
        await Assert.That(result.Data!.Items[1].Id).IsEqualTo(ids[1]);
    }

    [Test]
    public async Task Create_AddsEntity_AndReturnsProjectedResponse()
    {
        var (sender, uow, _) = CrudTestHarness.Create();

        var result = await sender.SendAsync(new CreateGadgetRequest
        {
            Name = "New Gadget",
            Description = "Fresh off the line",
            Stock = 10
        });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Id).IsNotEqualTo(Guid.Empty);

        var stored = uow.Entities<Gadget>();
        await Assert.That(stored.Count).IsEqualTo(1);
        await Assert.That(stored[0].Name).IsEqualTo("New Gadget");
        await Assert.That(stored[0].Description).IsEqualTo("Fresh off the line");
        await Assert.That(stored[0].Stock).IsEqualTo(10);
        await Assert.That(stored[0].Id).IsEqualTo(result.Data!.Id);
    }

    [Test]
    public async Task Patch_UpdatesOnlySetMembers()
    {
        var (sender, uow, _) = CrudTestHarness.Create();
        var gadget = new Gadget { Name = "Original", Stock = 1 };
        uow.Seed(gadget);

        var result = await sender.SendAsync(new PatchGadgetRequest { Id = gadget.Id, Stock = 99 });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Stock).IsEqualTo(99);
        await Assert.That(result.Data!.Name).IsEqualTo("Original");
        await Assert.That(gadget.Stock).IsEqualTo(99);
        await Assert.That(gadget.Name).IsEqualTo("Original");
    }

    [Test]
    public async Task Patch_UnknownId_ReturnsNotFound()
    {
        var (sender, _, _) = CrudTestHarness.Create();

        var result = await sender.SendAsync(new PatchGadgetRequest { Id = Guid.NewGuid(), Stock = 1 });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo("GADGET_NOT_FOUND");
    }

    [Test]
    public async Task Delete_RemovesEntity_ThenSecondDeleteIsNotFound()
    {
        var (sender, uow, _) = CrudTestHarness.Create();
        var gadget = new Gadget { Name = "ToDelete" };
        uow.Seed(gadget);

        var firstResult = await sender.SendAsync(new DeleteGadgetRequest { Id = gadget.Id });

        await Assert.That(firstResult.IsSuccess).IsTrue();
        await Assert.That(uow.Entities<Gadget>().Count).IsEqualTo(0);

        var secondResult = await sender.SendAsync(new DeleteGadgetRequest { Id = gadget.Id });

        await Assert.That(secondResult.IsError).IsTrue();
        await Assert.That(secondResult.Error!.ErrorCode).IsEqualTo("GADGET_NOT_FOUND");
    }

    [Test]
    public async Task HandwrittenHandler_WinsOverGenericHandler_ForSameRequestType()
    {
        var services = new ServiceCollection();
        var uow = new FakeUnitOfWork<TestDbContext>();
        services.AddSingleton<IUnitOfWork<TestDbContext>>(uow);

        services.AddSynaxMediator(options =>
        {
            options.AddHandlersFromAssembly(typeof(OverriddenGetGadgetHandler).Assembly);
            options.AddSynaxCrud<TestDbContext>(typeof(OverriddenGetGadgetRequest));
        });

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new OverriddenGetGadgetRequest { Id = Guid.NewGuid() });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Name).IsEqualTo("handwritten");
    }

    [Test]
    public async Task AddSynaxCrud_BadCreateRequest_ThrowsWithPropertyNameInMessage()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddSynaxMediator(options =>
                options.AddSynaxCrud<TestDbContext>(typeof(BadCreateGadgetRequest))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("NoSuchProperty");
    }

    [Test]
    public async Task AddSynaxCrud_EmptyPatchRequest_ThrowsMentioningPatchValue()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddSynaxMediator(options =>
                options.AddSynaxCrud<TestDbContext>(typeof(EmptyPatchGadgetRequest))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("PatchValue");
    }

    [Test]
    public async Task AddSynaxCrud_WrongKeyTypeRequest_ThrowsMentioningKeyType()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddSynaxMediator(options =>
                options.AddSynaxCrud<TestDbContext>(typeof(WrongKeyGetGadgetRequest))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Guid");
    }

    [Test]
    public async Task AddSynaxCrud_NonCrudType_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddSynaxMediator(options =>
                options.AddSynaxCrud<TestDbContext>(typeof(string))))
            .Throws<ArgumentException>();
    }
}
