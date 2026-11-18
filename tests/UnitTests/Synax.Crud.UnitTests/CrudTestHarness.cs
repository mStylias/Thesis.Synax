using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Mapper;
using Synax.EntityFrameworkCore.Testing;
using Synax.Mapper.Core;
using Synax.Mediator;

namespace Synax.Crud.UnitTests;

/// <summary>
/// Builds a mediator-backed harness around a <see cref="FakeUnitOfWork{TestDbContext}"/> seeded
/// with the Gadget CRUD requests, dispatched through <see cref="ISender"/> so the whole pipeline
/// (handler registration, explicit-handler skip rules, etc.) is exercised — not just the handler
/// class in isolation.
/// </summary>
internal static class CrudTestHarness
{
    internal static (ISender Sender, FakeUnitOfWork<TestDbContext> UnitOfWork, ServiceProvider Provider) Create(
        Action<MediatorOptions>? configureExtra = null)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<GadgetProfile>()).CreateMapper();
        var uow = new FakeUnitOfWork<TestDbContext>(new FakeUnitOfWorkOptions
        {
            Mapper = new SynaxMapperRepositoryMapper(mapper)
        });

        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork<TestDbContext>>(uow);

        services.AddSynaxMediator(options =>
        {
            options.AddSynaxCrud<TestDbContext>(
                typeof(GetGadgetRequest),
                typeof(ListGadgetsRequest),
                typeof(CreateGadgetRequest),
                typeof(PatchGadgetRequest),
                typeof(DeleteGadgetRequest));

            configureExtra?.Invoke(options);
        });

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        return (sender, uow, provider);
    }
}
