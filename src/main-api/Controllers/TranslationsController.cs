using Microsoft.AspNetCore.Mvc;

/// <summary>Publishes confirmed community translations.</summary>
[ApiController]
[Route("api/translations")]
public sealed class TranslationsController(TranslationService translations) : ControllerBase
{
    /// <summary>Returns confirmed translations with structured source coordinates.</summary>
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await translations.List());

    /// <summary>Exports the same confirmed set as UTF-8 CSV with a BOM.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export() => File(await translations.ExportCsv(), "text/csv", "translations.csv");
}
