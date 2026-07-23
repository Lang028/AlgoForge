# Geeked On — Build Guide (Phase 1: Photographer)

This gives you a real, from-scratch ASP.NET Core MVC project with Identity,
EF Core, and the full **Photographer** role wired up end to end. Everything
else in the use-case diagram (Event Coordinator, Attendee, Attendee Delegate,
the automated face pipeline) comes in the next phases — ask and I'll build
each one the same way.

## 0. Prerequisites

- .NET 8 SDK (`dotnet --version` should print `8.x`)
- SQL Server LocalDB (ships with Visual Studio, or install "SQL Server Express LocalDB" standalone)
- Visual Studio 2022 (17.8+) or VS Code with the C# Dev Kit

## 1. Get the files onto your machine

Unzip the archive I gave you. You should have:

```
AlgoForge/
  .gitignore
  AlgoForge/
    AlgoForge.csproj
    Program.cs
    appsettings.json
    appsettings.Development.json
    Models/
    Data/
    Controllers/
    ViewModels/Photographer/
    Views/
    wwwroot/
```

This *is* the project — there's no separate "create project" step needed,
because I already wrote the `.csproj` and `Program.cs` by hand to match
what `dotnet new mvc -au Individual` produces, but pre-wired for your domain
model instead of the default `WeatherForecast` template.

## 2. Restore packages

```bash
cd AlgoForge/AlgoForge
dotnet restore
```

This pulls down: `Identity.EntityFrameworkCore`, `Identity.UI`,
`EntityFrameworkCore.SqlServer`, `EntityFrameworkCore.Design`,
`EntityFrameworkCore.Tools`.

## 3. Scaffold the Identity UI pages (Login/Register)

I wired `Program.cs` to expect Identity's Razor Pages, but didn't hand-write
the actual Login/Register `.cshtml` pages (they're boilerplate — let the
tooling generate them so they stay in sync with the Identity package
version):

```bash
dotnet tool install -g dotnet-aspnet-codegenerator
dotnet add package Microsoft.VisualStudio.Web.CodeGeneration.Design

dotnet aspnet-codegenerator identity -dc AlgoForge.Data.ApplicationDbContext --userClass AlgoForge.Models.ApplicationUser
```

This drops scaffolded pages into `Areas/Identity/Pages/Account/`. Say yes
when it asks to overwrite `_Layout.cshtml` for Identity — or better, when
prompted, only select the Login/Register/Logout pages so it doesn't stomp
the main site layout I wrote.

## 4. Create the database

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

If `dotnet ef` isn't found: `dotnet tool install -g dotnet-ef`.

This creates every table from the domain model: `Organisations`, `Events`,
`EventMemberships`, `Invitations`, `Albums`, `Photos`, `FaceDetections`,
`FaceClusters`, `Tags`, `Connections`, `Comments`, plus Identity's own
`AspNetUsers` etc.

## 5. Seed one Organisation so you can test Create Event

The `CreateEvent` form needs at least one `Organisation` row to populate its
dropdown. Easiest path: add a tiny seed in `Program.cs` right before
`app.Run()`, or just insert one row manually:

```sql
INSERT INTO Organisations (Name, CreatedAt) VALUES ('Durban University of Technology', GETUTCDATE());
```

(Run this against `(localdb)\mssqllocaldb` / database `AlgoForge_GeekedOn`
via SSMS, Azure Data Studio, or VS's SQL Server Object Explorer.)

## 6. Run it

```bash
dotnet run
```

Visit the printed `https://localhost:xxxx` URL:

1. **Register** a new account (Identity handles this).
2. Go to **Photographer → Create Event** — pick the seeded organisation,
   name it, set a date. It's created in `Draft` status.
3. **Note:** uploads are blocked until an event is `Live`. Right now nothing
   flips that status — that's the Event Coordinator's "Configure Event" use
   case, coming in Phase 2. For now, flip it manually to test uploads:

```sql
UPDATE Events SET Status = 1 WHERE Id = 1; -- 1 = Live
```

4. Go to **Albums** → create an album → **Upload Photos**. Right now the
   upload handler writes placeholder blob URLs (see `UploadToBlobStorage` in
   `PhotographerController.cs`) — swap that for real Azure Blob Storage calls
   whenever you're ready to wire that in.
5. **Face Clusters** will be empty until the Python face worker exists
   (per your README, that's not built yet) or until you manually insert a
   `FaceCluster` + `FaceDetection` row for testing the Identify flow.

## What's implemented (Photographer — all 9 use cases from your diagram)

| Use case | Where |
|---|---|
| Create / Join Event | `CreateEvent`, `JoinEvent` actions |
| Upload Photographs | `UploadPhotos` |
| Organise Albums | `Albums`, `CreateAlbum` |
| Review Face Clusters | `FaceClusters` |
| Identify Face Cluster (Create Attendee Profile) | `IdentifyCluster` |
| Edit Attendee Profiles | `EditAttendeeProfile` |
| Invite Event Coordinator | `InviteCoordinator` |
| Work Across Multiple Organisations | `Index` dashboard (groups by org) |
| View Unidentified Clusters | `FaceClusters` (split into two lists) |

## What's deliberately stubbed (matches your README's roadmap)

- Blob storage upload is a placeholder — swap in `Azure.Storage.Blobs`.
- No Service Bus enqueue yet — the TODO comment marks exactly where it goes.
- The Python face worker isn't called — your README says it "hasn't landed
  yet," so there's nothing to integrate against.

## Next phases (say the word and I'll build these the same way)

1. **Event Coordinator** — Configure Event (Draft→Live→PostEvent→Archived),
   Invite Attendees by Email, Review & Edit Attendee Profiles, Moderate
   Comments, Manage Attendee Access.
2. **Attendee** — Browse/Filter Gallery, confirm/reject suggested Tags
   (the consent lifecycle), self-tag, manage privacy, Request/Manage
   Connections, Invite a Delegate.
3. **Attendee Delegate** — read-only browse + assist identifying.
4. **System (automated)** — this is really the Python face worker: detect
   faces, group into clusters, write `FaceDetection`/`FaceCluster` rows,
   trigger notifications.
