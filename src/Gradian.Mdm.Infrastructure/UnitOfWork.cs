using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Infrastructure.Data;
using Gradian.Mdm.Infrastructure.Repositories;

namespace Gradian.Mdm.Infrastructure
{
    /// <summary>
    /// Scoped (one per web request). Every repository shares the request's <see cref="DbSession"/>, so they use one
    /// connection and can join the same transaction. Repositories are created on first use.
    /// </summary>
    public sealed class UnitOfWork : IUnitOfWork
    {
        private readonly DbSession _session;
        private ProductMasterRepository? _productMaster;
        private ColumnEditSettingRepository? _columnEditSettings;
        private UserAccountRepository? _userAccounts;

        public UnitOfWork(DbSession session) => _session = session;

        public IProductMasterRepository ProductMasterRepository => _productMaster ??= new ProductMasterRepository(_session);
        public IColumnEditSettingRepository ColumnEditSettingRepository => _columnEditSettings ??= new ColumnEditSettingRepository(_session);
        public IUserAccountRepository UserAccountRepository => _userAccounts ??= new UserAccountRepository(_session);

        // The DI container disposes the session itself (it is registered separately); nothing else to release here.
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
