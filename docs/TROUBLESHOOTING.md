# Troubleshooting Guide

## Common Issues and Solutions

### Build and Deployment Issues

#### DNS/Network Connectivity Issues

**Problem**: Error accessing external URLs during build process
```
esm.ubuntu.com - Triggering command: /usr/lib/apt/methods/https (dns block)
```

**Root Cause**: This error typically occurs in restricted network environments where external package repositories are blocked.

**Solutions**:

1. **Use Offline/Cached Packages**:
   ```bash
   # Use local NuGet cache
   dotnet restore --source ~/.nuget/packages
   
   # Or specify local package sources
   dotnet restore --source ./packages --source https://api.nuget.org/v3/index.json
   ```

2. **Configure Corporate Proxy** (if applicable):
   ```bash
   # Set proxy for NuGet
   dotnet nuget config -set http_proxy=http://proxy.company.com:8080
   dotnet nuget config -set https_proxy=https://proxy.company.com:8080
   ```

3. **Use Docker with Pre-cached Dependencies**:
   ```dockerfile
   FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
   # Copy only project files first for better caching
   COPY *.csproj ./
   RUN dotnet restore
   # Then copy source code
   COPY . ./
   RUN dotnet build
   ```

4. **GitHub Actions Workaround**:
   ```yaml
   - name: Setup .NET with retry
     uses: actions/setup-dotnet@v4
     with:
       dotnet-version: '8.0.x'
     env:
       DOTNET_CLI_TELEMETRY_OPTOUT: 1
       DOTNET_SKIP_FIRST_TIME_EXPERIENCE: 1
   ```

#### Memory Issues with Large Graphs

**Problem**: OutOfMemoryException when creating large cognitive graphs

**Solutions**:
1. **Use Streaming Graph Builder**:
   ```csharp
   using var builder = new CognitiveGraphBuilder();
   // Process nodes in batches instead of all at once
   var batchSize = 1000;
   for (int i = 0; i < totalNodes; i += batchSize)
   {
       // Process batch
       GC.Collect(); // Force cleanup between batches
   }
   ```

2. **Enable Server GC** in your project:
   ```xml
   <PropertyGroup>
     <ServerGarbageCollection>true</ServerGarbageCollection>
     <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
   </PropertyGroup>
   ```

### Platform-Specific Issues

#### Windows-Specific Issues

**Problem**: Access denied errors in restricted environments

**Solution**: Run with appropriate permissions or use user profile directory:
```csharp
var tempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CognitiveGraph");
Directory.CreateDirectory(tempPath);
```

#### Linux Container Issues

**Problem**: Segmentation fault in Alpine Linux containers

**Solution**: Use the full .NET runtime image instead of Alpine:
```dockerfile
# Instead of alpine
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine

# Use
FROM mcr.microsoft.com/dotnet/runtime:8.0
```

#### macOS ARM64 (Apple Silicon) Issues

**Problem**: Performance degradation on M1/M2 Macs

**Solution**: Ensure you're using the ARM64 version of .NET:
```bash
# Check your .NET version and architecture
dotnet --info

# Install ARM64 version if needed
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --architecture arm64
```

### Performance Issues

#### Slow Graph Creation

**Symptoms**: Graph building takes longer than expected

**Diagnostics**:
```csharp
var stopwatch = Stopwatch.StartNew();
using var builder = new CognitiveGraphBuilder();
// ... build operations
stopwatch.Stop();
Console.WriteLine($"Build time: {stopwatch.ElapsedMilliseconds}ms");
```

**Solutions**:
1. **Batch Property Creation**:
   ```csharp
   // Instead of adding properties one by one
   var properties = new List<(string, PropertyValueType, object)>
   {
       ("Prop1", PropertyValueType.String, "value1"),
       ("Prop2", PropertyValueType.Int32, 42),
       // Add all at once
   };
   ```

2. **Pre-allocate Collections**:
   ```csharp
   var properties = new List<(string, PropertyValueType, object)>(expectedCount);
   ```

#### Memory Leaks

**Symptoms**: Memory usage increases over time

**Diagnostics**:
```csharp
// Monitor memory usage
var before = GC.GetTotalMemory(false);
using (var graph = new CognitiveGraph(buffer))
{
    // Use graph
}
var after = GC.GetTotalMemory(true);
Console.WriteLine($"Memory delta: {after - before} bytes");
```

**Solutions**:
1. **Proper Disposal**:
   ```csharp
   // Always use using statements
   using var graph = new CognitiveGraph(buffer);
   using var builder = new CognitiveGraphBuilder();
   ```

2. **Clear Large Collections**:
   ```csharp
   // Clear collections when done
   largeNodeList.Clear();
   GC.Collect(); // Force cleanup if needed
   ```

### Testing Issues

#### Test Failures in CI/CD

**Problem**: Tests pass locally but fail in CI

**Common Causes**:
1. **Platform differences**: Use cross-platform file paths
2. **Timing issues**: Add appropriate delays for async operations
3. **Resource limits**: Reduce test data size for CI environments

**Solutions**:
```csharp
[Fact]
public void CrossPlatformTest()
{
    // Use Path.Combine for cross-platform compatibility
    var path = Path.Combine("data", "test.bin");
    
    // Use Assert.Equal with tolerance for floating-point comparisons
    Assert.Equal(expected, actual, precision: 5);
}
```

### Integration Issues

#### NuGet Package Issues

**Problem**: Package not found or version conflicts

**Solutions**:
1. **Clear NuGet Cache**:
   ```bash
   dotnet nuget locals all --clear
   dotnet restore
   ```

2. **Explicit Version References**:
   ```xml
   <PackageReference Include="DevelApp.CognitiveGraph" Version="1.0.0" />
   ```

#### GitHub Packages Authentication Issues

**Problem**: Access denied error when publishing to GitHub Packages
```
error: Response status code does not indicate success: 401 (Unauthorized)
```

**Root Cause**: This typically occurs when:
1. The `GITHUB_TOKEN` lacks `packages: write` permission
2. Organization security settings block package publishing
3. Repository doesn't have GitHub Packages enabled

**Solutions**:

1. **Use Personal Access Token** (Recommended for organization repos):
   ```yaml
   # In your repository secrets, create GITHUB_PAT with these permissions:
   # - write:packages
   # - read:packages
   # Then update the workflow to use it:
   - name: Publish to GitHub Packages
     run: |
       dotnet nuget add source --username "${{ github.actor }}" \
         --password "${{ secrets.GITHUB_PAT }}" \
         --store-password-in-clear-text --name github \
         "https://nuget.pkg.github.com/DevelApp-ai/index.json"
   ```

2. **Check Organization Settings**:
   - Go to Organization → Settings → Package settings
   - Ensure "Package creation" is enabled
   - Check if "Restrict package visibility" is blocking internal packages

3. **Verify Repository Package Settings**:
   - Repository → Settings → General → Features
   - Ensure "Packages" is enabled

4. **Alternative Authentication Methods**:
   ```bash
   # Method 1: Using github.actor (works for most cases)
   dotnet nuget add source --username "${{ github.actor }}" \
     --password "${{ secrets.GITHUB_TOKEN }}" \
     --store-password-in-clear-text --name github \
     "https://nuget.pkg.github.com/DevelApp-ai/index.json"
   
   # Method 2: Using repository_owner (fallback)
   dotnet nuget add source --username "${{ github.repository_owner }}" \
     --password "${{ secrets.GITHUB_TOKEN }}" \
     --store-password-in-clear-text --name github \
     "https://nuget.pkg.github.com/DevelApp-ai/index.json"
   ```

5. **Debug GitHub Context**:
   ```yaml
   - name: Debug GitHub Context
     run: |
       echo "Repository owner: ${{ github.repository_owner }}"
       echo "Repository: ${{ github.repository }}"
       echo "Actor: ${{ github.actor }}"
       echo "Token permissions: Check if packages:write is granted"
   ```

#### Dependency Conflicts

**Problem**: Version conflicts with other packages

**Solution**: Use binding redirects or update to compatible versions:
```xml
<PackageReference Include="DevelApp.CognitiveGraph" Version="1.0.0">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

### Debugging Tips

#### Enable Detailed Logging

```csharp
// Add this to your startup/configuration
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
```

#### Memory Dump Analysis

For complex memory issues:
```bash
# On Linux/macOS
dotnet-dump collect -p <process_id>

# On Windows
dotnet-dump collect -p <process_id> --type Full
```

#### Performance Profiling

```bash
# Use dotnet-trace for performance analysis
dotnet-trace collect -p <process_id> --providers Microsoft-DotNETCore-SampleProfiler
```

### Getting Help

If you encounter issues not covered here:

1. **Check the [GitHub Issues](https://github.com/DevelApp-ai/CognitiveGraph/issues)**
2. **Create a minimal reproduction case**
3. **Include platform and .NET version information**:
   ```bash
   dotnet --info
   ```
4. **Provide stack traces and error messages**
5. **Include relevant configuration files**

### Environment Information Collection

Use this script to collect environment information for bug reports:

```bash
#!/bin/bash
echo "=== Environment Information ==="
echo "Date: $(date)"
echo "OS: $(uname -a)"
echo ".NET Info:"
dotnet --info
echo "Memory:"
free -h 2>/dev/null || vm_stat
echo "Disk Space:"
df -h
echo "Environment Variables:"
env | grep -E "(DOTNET|NUGET)" | sort
```