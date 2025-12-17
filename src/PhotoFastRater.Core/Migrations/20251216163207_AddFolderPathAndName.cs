using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoFastRater.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderPathAndName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cameras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Make = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    PhotoCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cameras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    CoverPhotoPath = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExportTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OutputWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    MaintainAspectRatio = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableFrame = table.Column<bool>(type: "INTEGER", nullable: false),
                    FrameWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    FrameColor = table.Column<string>(type: "TEXT", nullable: false),
                    EnableExifOverlay = table.Column<bool>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayFields = table.Column<string>(type: "TEXT", nullable: false),
                    FontFamily = table.Column<string>(type: "TEXT", nullable: false),
                    FontSize = table.Column<int>(type: "INTEGER", nullable: false),
                    TextColor = table.Column<string>(type: "TEXT", nullable: false),
                    BackgroundColor = table.Column<string>(type: "TEXT", nullable: false),
                    BackgroundOpacity = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetPlatform = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FolderExclusionPatterns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Pattern = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderExclusionPatterns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    PhotoCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    IsRecursive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScanDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PhotoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    DateTaken = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImportDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRejected = table.Column<bool>(type: "INTEGER", nullable: false),
                    CameraModel = table.Column<string>(type: "TEXT", nullable: true),
                    CameraMake = table.Column<string>(type: "TEXT", nullable: true),
                    LensModel = table.Column<string>(type: "TEXT", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Aperture = table.Column<double>(type: "REAL", nullable: true),
                    ShutterSpeed = table.Column<string>(type: "TEXT", nullable: true),
                    ISO = table.Column<int>(type: "INTEGER", nullable: true),
                    FocalLength = table.Column<double>(type: "REAL", nullable: true),
                    ExposureCompensation = table.Column<double>(type: "REAL", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    LocationName = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailCachePath = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailGeneratedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FileHash = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhotoEventMappings",
                columns: table => new
                {
                    PhotoId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoEventMappings", x => new { x.PhotoId, x.EventId });
                    table.ForeignKey(
                        name: "FK_PhotoEventMappings_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoEventMappings_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_Make_Model",
                table: "Cameras",
                columns: new[] { "Make", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_EndDate",
                table: "Events",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_Events_StartDate",
                table: "Events",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_FolderExclusionPatterns_IsEnabled",
                table: "FolderExclusionPatterns",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_Lenses_Model",
                table: "Lenses",
                column: "Model",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedFolders_FolderPath",
                table: "ManagedFolders",
                column: "FolderPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedFolders_IsActive",
                table: "ManagedFolders",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedFolders_LastScanDate",
                table: "ManagedFolders",
                column: "LastScanDate");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoEventMappings_EventId",
                table: "PhotoEventMappings",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_CameraModel",
                table: "Photos",
                column: "CameraModel");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_DateTaken",
                table: "Photos",
                column: "DateTaken");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_FileHash",
                table: "Photos",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_FolderPath",
                table: "Photos",
                column: "FolderPath");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_Rating",
                table: "Photos",
                column: "Rating");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cameras");

            migrationBuilder.DropTable(
                name: "ExportTemplates");

            migrationBuilder.DropTable(
                name: "FolderExclusionPatterns");

            migrationBuilder.DropTable(
                name: "Lenses");

            migrationBuilder.DropTable(
                name: "ManagedFolders");

            migrationBuilder.DropTable(
                name: "PhotoEventMappings");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Photos");
        }
    }
}
