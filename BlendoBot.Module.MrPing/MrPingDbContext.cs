using Microsoft.EntityFrameworkCore;
using System.IO;

namespace BlendoBot.Module.MrPing;
internal class MrPingDbContext : DbContext {
	private MrPingDbContext(DbContextOptions<MrPingDbContext> options) : base(options) { }
	public DbSet<UserStats> UserStats { get; set; }

	public static MrPingDbContext Get(MrPing module) {
		DbContextOptionsBuilder<MrPingDbContext> optionsBuilder = new();
		optionsBuilder.UseSqlite($"Data Source={Path.Combine(module.FilePathProvider.GetDataDirectoryPath(module), "blendobot-mrping-database.db")}");
		MrPingDbContext dbContext = new(optionsBuilder.Options);
		dbContext.Database.EnsureCreated();
		return dbContext;
	}
}
