namespace Gradian.Mdm.Web.Models
{
    public class UserEditViewModel
    {
        public bool IsNew { get; set; }
        public string Login { get; set; } = "";
        public string? DisplayName { get; set; }
        public int LevelNo { get; set; }
        public bool IsActive { get; set; }
        public string? Password { get; set; }   // required for new users; optional reset for existing ones
    }
}
