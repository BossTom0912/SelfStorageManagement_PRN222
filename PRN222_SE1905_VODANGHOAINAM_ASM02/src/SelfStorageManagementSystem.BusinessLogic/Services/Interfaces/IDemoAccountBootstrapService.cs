namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IDemoAccountBootstrapService
{
    Task BootstrapDemoAccountsAsync(CancellationToken cancellationToken = default);
}
