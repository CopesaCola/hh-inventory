namespace Inventory.Web.Services;

public interface ICurrentUser
{
    string Name { get; }
}

public class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public string Name
    {
        get
        {
            var name = _accessor.HttpContext?.User?.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(name)) return name!;

            // Fallback for non-authenticated dev runs
            return Environment.UserDomainName + "\\" + Environment.UserName;
        }
    }
}
