using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services.Helpers;

public class InvoicesDownloadHelper(
    IFileService fileService,
    IOptions<DataDirectories> dataDirectories,
    HttpClient httpClient)
{
    public async Task<IActionResult> Process(List<PayrollInvoice> invoices, Uri absoluteRootUri)
    {
        var zipName = $"PayrollInvoices-{DateTime.Now:yyyy_MM_dd-HH_mm_ss}.zip";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var usedFilenames = new HashSet<string>();

            foreach (var invoice in invoices)
            {
                var allFiles = new List<string> { invoice.InvoiceFilename };
                if (!string.IsNullOrWhiteSpace(invoice.ExtraFilenames))
                    allFiles.AddRange(invoice.ExtraFilenames.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()));

                foreach (var file in allFiles)
                {
                    var fileUrl = await fileService.GetFileUrl(absoluteRootUri, file);
                    var filename = Path.GetFileName(fileUrl);
                    byte[] fileBytes;

                    if (fileUrl?.Contains("/LocalStorage/") == true)
                        fileBytes = await File.ReadAllBytesAsync(Path.Combine(dataDirectories.Value.StorageDir, filename));
                    else
                        fileBytes = await httpClient.DownloadFileAsByteArray(fileUrl);

                    if (filename?.Length > 36)
                    {
                        var first36 = filename.Substring(0, 36);
                        if (Guid.TryParse(first36, out _))
                        {
                            var newName = $"{invoice.User.Name} - {invoice.CreatedAt:yyyy-MM}";
                            filename = filename.Replace(first36, newName);

                            // Ensure filename is unique
                            var baseFilename = Path.GetFileNameWithoutExtension(filename);
                            var extension = Path.GetExtension(filename);
                            var counter = 1;

                            while (usedFilenames.Contains(filename))
                            {
                                filename = $"{baseFilename} ({counter}){extension}";
                                counter++;
                            }

                            usedFilenames.Add(filename);
                        }
                    }

                    var entry = zip.CreateEntry(filename);
                    using (var entryStream = entry.Open())
                    {
                        await entryStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                    }
                }
            }
        }

        ms.Position = 0;
        return new FileContentResult(ms.ToArray(), "application/zip") { FileDownloadName = zipName };
    }
}
