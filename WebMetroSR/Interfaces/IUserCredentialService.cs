namespace WebMetroSR.Interfaces
{
    public interface IUserCredentialService
    {
        void SetCredentials(string username, string password); // пока пароль не используется, но сохраняем на будущее
        string? GetUsername(); // возвращает login (без домена)
        bool IsAuthenticated { get; }
        void Clear();
    }
}