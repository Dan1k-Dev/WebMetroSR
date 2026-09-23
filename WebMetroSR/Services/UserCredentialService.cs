using Microsoft.AspNetCore.Http;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Services
{
    public class UserCredentialService(IHttpContextAccessor httpContextAccessor) : IUserCredentialService
    {
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

        private ISession? Session => _httpContextAccessor.HttpContext?.Session;

        public void SetCredentials(string username, string password)
        {
            var session = Session ?? throw new InvalidOperationException("Session not available");
            session.SetString("Username", username);
            session.SetString("Password", password);
        }

        public string? GetUsername()
        {
            var session = Session;
            if (session == null) return null;
            return session.GetString("Username");
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(GetUsername());

        public void Clear()
        {
            var session = Session;
            session?.Clear();
        }
    }
}