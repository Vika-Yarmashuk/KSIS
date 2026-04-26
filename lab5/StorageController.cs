using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace FileStorage.Controllers;


[Route("{*path}")]
public class StorageController : ControllerBase
{
    private readonly string _storageRoot;
   

    public StorageController(IConfiguration configuration)
    {
        // Корневая папка хранилища
        _storageRoot = configuration["Storage:RootPath"];

            if (!Directory.Exists(_storageRoot))
                Directory.CreateDirectory(_storageRoot);
       
    }


    // Преобразует относительный URL‑путь в физический путь к файлу/каталогу,

    private string GetPhysicalPath(string path)
    {
        path = path?.Replace('/', Path.DirectorySeparatorChar) ?? string.Empty;
        path = path.TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_storageRoot, path));

        if (!fullPath.StartsWith(_storageRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal attempt detected.");

        return fullPath;
    }

    
    // Определяет Content-Type по расширению файла.
  
    private string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    // Формирует JSON‑список содержимого каталога.
    private object GetDirectoryListing(string physicalPath)
    {
        var dirInfo = new DirectoryInfo(physicalPath);
        return dirInfo.GetFileSystemInfos().Select(fsi => new
        {
            name = fsi.Name,
            type = fsi is DirectoryInfo ? "directory" : "file",
            size = (fsi as FileInfo)?.Length,
            lastModified = fsi.LastWriteTimeUtc
        });
    }
   // Проверяет доступен ли диск для создания корневой папки
    private void EnsureStorageRoot()
    {
        if (!Directory.Exists(_storageRoot))
        {
            try
            {
                Directory.CreateDirectory(_storageRoot);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cannot access storage root: {_storageRoot}", ex);
            }
        }
    }

    //  PUT 
    [HttpPut]
    public async Task<IActionResult> Put(string path)
    {
        try
        {
            var physicalPath = GetPhysicalPath(path);

            // Нельзя перезаписать каталог файлом
            if (Directory.Exists(physicalPath))
                return Conflict("Cannot overwrite a directory with a file.");

            var directory = Path.GetDirectoryName(physicalPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            bool existed = System.IO.File.Exists(physicalPath);

            await using (var fileStream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write))
            {
                await Request.Body.CopyToAsync(fileStream);
            }

            return existed ? Ok() : StatusCode(201);
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest("Invalid path.");
        }
        catch (Exception ex)
        {
          
            return StatusCode(500);
        }
    }

    // GET 
    [HttpGet]
    public async Task<IActionResult> Get(string path)
    {
       
       
        try
        {
            Console.WriteLine(path);
            var physicalPath = GetPhysicalPath(path);

            if (System.IO.File.Exists(physicalPath))
            {
                var contentType = GetContentType(physicalPath);
                return PhysicalFile(physicalPath, contentType);
            }

            if (Directory.Exists(physicalPath))
            {
                var listing = GetDirectoryListing(physicalPath);
                return Ok(listing);
            }

            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest("Invalid path.");
        }
        catch (Exception ex)
        {
           
            return StatusCode(500);
        }
    }

    //  HEAD 
    [HttpHead]
    public IActionResult Head(string path)
    {
       
        try
        {
            var physicalPath = GetPhysicalPath(path);

            if (System.IO.File.Exists(physicalPath))
            {
                var fileInfo = new FileInfo(physicalPath);
                Response.Headers.Add("Content-Length", fileInfo.Length.ToString());
                Response.Headers.Add("Last-Modified", fileInfo.LastWriteTimeUtc.ToString("R"));
                return Ok();
            }

            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest("Invalid path.");
        }
        catch (Exception ex)
        {
         
            return StatusCode(500);
        }
    }

    //  DELETE
    [HttpDelete]
    public IActionResult Delete(string path)
    {
      
        try
        {
            var physicalPath = GetPhysicalPath(path);

            if (System.IO.File.Exists(physicalPath))
            {
                System.IO.File.Delete(physicalPath);
                return Ok();
            }

            if (Directory.Exists(physicalPath))
            {
                Directory.Delete(physicalPath, recursive: true);
                return Ok();
            }

            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest("Invalid path.");
        }
        catch (Exception ex)
        {
          
            return StatusCode(500);
        }
    }
}