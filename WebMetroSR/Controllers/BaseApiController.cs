using Microsoft.AspNetCore.Mvc;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class BaseApiController : ControllerBase
    {
        /// <summary>
        /// Проверяет, что все переданные строки не null и не пусты.
        /// </summary>
        protected IActionResult? EnsureRequired(params (string value, string paramName)[] parameters)
        {
            foreach (var (value, paramName) in parameters)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return BadRequest($"Параметр '{paramName}' обязателен.");
            }
            return null;
        }

        /// <summary>
        /// Проверяет, авторизован ли пользователь.
        /// </summary>
        protected IActionResult? EnsureAuthenticated(IUserCredentialService userCreds)
        {
            if (!userCreds.IsAuthenticated)
                return Unauthorized("Необходима авторизация");
            return null;
        }
    }
}