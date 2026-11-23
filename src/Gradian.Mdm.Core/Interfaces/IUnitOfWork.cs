namespace Gradian.Mdm.Core.Interfaces
{
    /// <summary>
    /// Gives access to the repositories over one database connection per web request.
    /// The connection is opened on first use and closed when the request ends.
    /// </summary>
    public interface IUnitOfWork : IDisposable, IAsyncDisposable
    {
        IProductMasterRepository ProductMasterRepository { get; }
        IColumnEditSettingRepository ColumnEditSettingRepository { get; }
        IUserAccountRepository UserAccountRepository { get; }
    }
}
