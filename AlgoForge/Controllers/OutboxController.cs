using AlgoForge.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlgoForge.Controllers
{
    // Demo-only mailbox: lists what OutboxEmailSender wrote so invite, claim and
    // connection links can be opened during a presentation. Admin-only because the
    // emails contain other people's addresses and single-use tokens -- anyone who can
    // read this page can claim any invite in it.
    [Authorize(Roles = DbInitializer.SystemAdminRole)]
    public class OutboxController : Controller
    {
        private readonly string _outboxRoot;

        public OutboxController(IWebHostEnvironment environment)
        {
            _outboxRoot = Path.Combine(environment.ContentRootPath, "App_Data", "outbox");
        }

        public IActionResult Index()
        {
            if (!Directory.Exists(_outboxRoot))
            {
                return View(new List<OutboxItem>());
            }

            var items = new DirectoryInfo(_outboxRoot)
                .GetFiles("*.html")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f =>
                {
                    var parts = Path.GetFileNameWithoutExtension(f.Name).Split("__");
                    return new OutboxItem
                    {
                        FileName = f.Name,
                        SentAt = f.CreationTime,
                        To = parts.Length > 1 ? parts[1] : "(unknown)",
                        Subject = parts.Length > 2 ? parts[2] : Path.GetFileNameWithoutExtension(f.Name)
                    };
                })
                .ToList();

            return View(items);
        }

        [HttpGet]
        [Route("Outbox/View/{name}")]
        public IActionResult Message(string name)
        {
            // Only a bare file name inside the outbox -- no traversal out of it.
            if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || !name.EndsWith(".html"))
            {
                return NotFound();
            }

            var path = Path.Combine(_outboxRoot, name);
            return System.IO.File.Exists(path)
                ? Content(System.IO.File.ReadAllText(path), "text/html")
                : NotFound();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Clear()
        {
            if (Directory.Exists(_outboxRoot))
            {
                foreach (var file in Directory.GetFiles(_outboxRoot, "*.html"))
                {
                    System.IO.File.Delete(file);
                }
            }

            return RedirectToAction(nameof(Index));
        }

        public class OutboxItem
        {
            public string FileName { get; set; } = string.Empty;
            public string To { get; set; } = string.Empty;
            public string Subject { get; set; } = string.Empty;
            public DateTime SentAt { get; set; }
        }
    }
}
