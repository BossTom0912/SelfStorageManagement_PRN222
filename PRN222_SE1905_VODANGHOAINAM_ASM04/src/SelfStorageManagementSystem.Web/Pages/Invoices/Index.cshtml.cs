using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SelfStorageManagementSystem.Web.Pages.Invoices;

[Authorize]
public class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    public IActionResult OnGet()
    {
        if (Id.HasValue && Id.Value > 0)
        {
            return RedirectToPage("Details", new { id = Id.Value });
        }

        return Page();
    }
}
