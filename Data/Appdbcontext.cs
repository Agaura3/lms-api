using Microsoft.EntityFrameworkCore;
using lms_api.Models;

namespace lms_api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // ==============================
    // Core Tables
    // ==============================
    public DbSet<User> Users { get; set; }
    public DbSet<Leave> Leaves { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    // ==============================
    // SaaS Core
    // ==============================
    public DbSet<Plan> Plans { get; set; }
    public DbSet<CompanySubscription> CompanySubscriptions { get; set; }

    // ==============================
    // v2 Modules
    // ==============================
    public DbSet<LeavePolicy> LeavePolicies { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<EmailQueue> EmailQueues { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =====================================================
        // 🔹 Relationship Configuration
        // =====================================================

        modelBuilder.Entity<CompanySubscription>()
            .HasOne(cs => cs.Company)
            .WithOne(c => c.Subscription)
            .HasForeignKey<CompanySubscription>(cs => cs.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CompanySubscription>()
            .HasOne(cs => cs.Plan)
            .WithMany(p => p.Subscriptions)
            .HasForeignKey(cs => cs.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LeavePolicy>()
            .HasOne(lp => lp.Company)
            .WithMany()
            .HasForeignKey(lp => lp.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);


        // =====================================================
        // 🔥 Enterprise Performance Indexing
        // =====================================================

        // --------------------------
        // User Indexes
        // --------------------------
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.CompanyId, u.Department });


        // --------------------------
        // Leave Indexes (Reporting Heavy)
        // --------------------------

        // Dashboard & Date Filters
        modelBuilder.Entity<Leave>()
            .HasIndex(l => new { l.CompanyId, l.StartDate });

        // Status based filtering
        modelBuilder.Entity<Leave>()
            .HasIndex(l => new { l.CompanyId, l.Status });

        // Leave type analytics
        modelBuilder.Entity<Leave>()
            .HasIndex(l => new { l.CompanyId, l.LeaveType });

        // Employee breakdown
        modelBuilder.Entity<Leave>()
            .HasIndex(l => l.UserId);

        // Trend comparison optimization
        modelBuilder.Entity<Leave>()
            .HasIndex(l => new { l.CompanyId, l.StartDate, l.Status });


        // --------------------------
        // LeavePolicy
        // --------------------------
        modelBuilder.Entity<LeavePolicy>()
            .HasIndex(lp => new { lp.CompanyId, lp.LeaveTypeName })
            .IsUnique();


        // --------------------------
        // Notifications
        // --------------------------
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead });


        // --------------------------
        // Company
        // --------------------------
        modelBuilder.Entity<Company>()
            .HasIndex(c => c.Name)
            .IsUnique();


        // --------------------------
        // Email Queue Optimization
        // --------------------------
        modelBuilder.Entity<EmailQueue>()
            .HasIndex(e => e.Status);

        modelBuilder.Entity<EmailQueue>()
            .HasIndex(e => e.CreatedAt);

        modelBuilder.Entity<EmailQueue>()
            .HasIndex(e => new { e.Status, e.RetryCount });
    }
}