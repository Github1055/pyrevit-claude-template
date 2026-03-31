using SmartFiler.Data;
using SmartFiler.Services;

namespace SmartFiler.Tests;

public class FileCategorizer_Tests
{
    // ── Revit ──────────────────────────────────────────────

    [Theory]
    [InlineData("MyProject.rvt", FileCategory.RevitProject)]
    [InlineData("Building_Model.rvt", FileCategory.RevitProject)]
    [InlineData("Project.0001.rvt", FileCategory.RevitBackup)]
    [InlineData("Project.0042.rvt", FileCategory.RevitBackup)]
    [InlineData("Door.rfa", FileCategory.RevitFamily)]
    [InlineData("Window_Type_A.rfa", FileCategory.RevitFamily)]
    [InlineData("Door.0003.rfa", FileCategory.RfaBackup)]
    [InlineData("Window.0001.rfa", FileCategory.RfaBackup)]
    [InlineData("Template.rte", FileCategory.RevitTemplate)]
    [InlineData("Office_Standard.rte", FileCategory.RevitTemplate)]
    public void Categorize_RevitFiles_ReturnsCorrectCategory(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    // ── Blender ────────────────────────────────────────────

    [Theory]
    [InlineData("scene.blend", FileCategory.Blender)]
    [InlineData("MyScene.blend", FileCategory.Blender)]
    public void Categorize_BlenderFile_ReturnsBlender(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    [Theory]
    [InlineData("scene.blend1", FileCategory.BlenderBackup)]
    [InlineData("scene.blend2", FileCategory.BlenderBackup)]
    [InlineData("scene.blend15", FileCategory.BlenderBackup)]
    public void Categorize_BlenderBackupFile_ReturnsBlenderBackup(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    // ── CAD / 3D ───────────────────────────────────────────

    [Theory]
    [InlineData("floorplan.dwg", FileCategory.AutoCad)]
    [InlineData("section.dxf", FileCategory.AutoCad)]
    public void Categorize_AutoCadFiles_ReturnsAutoCad(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    [Fact]
    public void Categorize_RhinoFile_ReturnsRhino()
    {
        Assert.Equal(FileCategory.Rhino, FileCategorizer.Categorize("model.3dm"));
    }

    [Fact]
    public void Categorize_PlasticityFile_ReturnsPlasticity()
    {
        Assert.Equal(FileCategory.Plasticity, FileCategorizer.Categorize("object.plasticity"));
    }

    [Theory]
    [InlineData("model.fbx", FileCategory.ThreeDInterchange)]
    [InlineData("mesh.obj", FileCategory.ThreeDInterchange)]
    [InlineData("scene.glb", FileCategory.ThreeDInterchange)]
    [InlineData("scene.gltf", FileCategory.ThreeDInterchange)]
    [InlineData("part.stl", FileCategory.ThreeDInterchange)]
    public void Categorize_ThreeDInterchangeFiles_ReturnsThreeDInterchange(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    // ── Documents & Images ─────────────────────────────────

    [Theory]
    [InlineData("report.pdf", FileCategory.Document)]
    [InlineData("schedule.docx", FileCategory.Document)]
    [InlineData("budget.xlsx", FileCategory.Document)]
    public void Categorize_DocumentFiles_ReturnsDocument(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    [Theory]
    [InlineData("photo.png", FileCategory.Image)]
    [InlineData("render.jpg", FileCategory.Image)]
    public void Categorize_ImageFiles_ReturnsImage(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    // ── Web Links & Shortcuts ──────────────────────────────

    [Fact]
    public void Categorize_UrlFile_ReturnsWebLink()
    {
        Assert.Equal(FileCategory.WebLink, FileCategorizer.Categorize("bookmark.url"));
    }

    [Fact]
    public void Categorize_LnkFile_ReturnsShortcut()
    {
        Assert.Equal(FileCategory.Shortcut, FileCategorizer.Categorize("app.lnk"));
    }

    // ── Executables: Installer vs Driver ───────────────────

    [Theory]
    [InlineData("BlenderSetup.exe", FileCategory.Installer)]
    [InlineData("revit_installer_2024.exe", FileCategory.Installer)]
    public void Categorize_ExeWithSetup_ReturnsInstaller(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    [Theory]
    [InlineData("nvidia_driver_550.exe", FileCategory.Driver)]
    [InlineData("chipset_update.exe", FileCategory.Driver)]
    public void Categorize_ExeWithDriver_ReturnsDriver(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    // ── Archives ───────────────────────────────────────────

    [Fact]
    public void Categorize_ZipFile_ReturnsArchive()
    {
        Assert.Equal(FileCategory.Archive, FileCategorizer.Categorize("project.zip"));
    }

    // ── Edge Cases ─────────────────────────────────────────

    [Theory]
    [InlineData("mystery.xyz", FileCategory.Other)]
    [InlineData("data.bin", FileCategory.Other)]
    public void Categorize_UnknownExtension_ReturnsOther(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    [Fact]
    public void Categorize_EmptyFilename_ReturnsOther()
    {
        Assert.Equal(FileCategory.Other, FileCategorizer.Categorize(""));
    }

    [Fact]
    public void Categorize_WhitespaceFilename_ReturnsOther()
    {
        Assert.Equal(FileCategory.Other, FileCategorizer.Categorize("   "));
    }

    [Fact]
    public void Categorize_NoExtension_ReturnsOther()
    {
        Assert.Equal(FileCategory.Other, FileCategorizer.Categorize("README"));
    }

    // ── Case insensitivity ─────────────────────────────────

    [Theory]
    [InlineData("Model.RVT", FileCategory.RevitProject)]
    [InlineData("scene.BLEND", FileCategory.Blender)]
    [InlineData("plan.DWG", FileCategory.AutoCad)]
    public void Categorize_UpperCaseExtension_StillMatchesCorrectly(string fileName, FileCategory expected)
    {
        Assert.Equal(expected, FileCategorizer.Categorize(fileName));
    }

    // ── Exe without keywords → Other (not Installer/Driver) ─

    [Fact]
    public void Categorize_PlainExe_ReturnsOther()
    {
        Assert.Equal(FileCategory.Other, FileCategorizer.Categorize("myapp.exe"));
    }
}
