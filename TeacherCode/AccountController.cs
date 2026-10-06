using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Data;
using zPayment.Services.VuLNS.System;

namespace zPayment.MVCWebApp.VuLNS.Controllers
{
    public class AccountController : Controller
    {
        private readonly SystemUserAccountService _userAccountService;

        public AccountController(SystemUserAccountService systemUserAccountService) => _userAccountService = systemUserAccountService; 

        public IActionResult Index()
        {
            return RedirectToAction("Login");
            //return View();
        }

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string userName, string password)
        {
            try
            {
                var userAccount = await _userAccountService.GetUserAccount(userName, password);

                if (userAccount != null)
                {
                    var claims = new List<Claim>
                                {
                                    new Claim(ClaimTypes.Name, userAccount.UserName),
                                    new Claim(ClaimTypes.Role, userAccount.RoleId.ToString())
                                };

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

                    Response.Cookies.Append("UserName", userAccount.FullName);
                    Response.Cookies.Append("Role", userAccount.RoleId.ToString());

                    return RedirectToAction("Index", "CategoryBankAccounts");
                }                
            }
            catch (Exception ex)
            {

            }

            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ModelState.AddModelError("", "Login failure");
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete("UserName");
            Response.Cookies.Delete("Role");
            return RedirectToAction("Login", "Account");
        }

        public async Task<IActionResult> Forbidden()
        {
            return View();
        }
    }
}
