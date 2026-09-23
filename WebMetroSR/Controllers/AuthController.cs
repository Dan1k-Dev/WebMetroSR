using Microsoft.AspNetCore.Mvc;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserCredentialService _userCreds;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IUserCredentialService userCreds, ILogger<AuthController> logger)
        {
            _userCreds = userCreds;
            _logger = logger;
        }

        [HttpPost("login")]
        public IActionResult Login([FromForm] string login, [FromForm] string password)
        {
            try
            {
                var username = login?.Split('@')[0] ?? login;
                if (string.IsNullOrWhiteSpace(username))
                    return BadRequest(new { success = false, error = "Логин обязателен" });

                _userCreds.SetCredentials(username, password ?? string.Empty);
                _logger.LogInformation("Пользователь {Username} авторизован", username);

                // Проверяем, что данные сохранились
                var check = _userCreds.GetUsername();
                _logger.LogInformation("Проверка: сохранённый логин = {Check}", check);

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при авторизации");
                return StatusCode(500, new { success = false, error = "Внутренняя ошибка" });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            _userCreds.Clear();
            return Ok(new { success = true });
        }

        [HttpGet("check")]
        public IActionResult Check()
        {
            var username = _userCreds.GetUsername();
            var isAuth = !string.IsNullOrEmpty(username);
            _logger.LogDebug("Check auth: {IsAuth}, user: {User}", isAuth, username);
            return Ok(new { authenticated = isAuth, username = username });
        }
    }
}