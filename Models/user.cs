using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using lms_api.Models.Enums;

namespace lms_api.Models;
    public class User
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string Email { get; set; } = null!;

        [Required]
        public string PasswordHash { get; set; } = null!;

        [Required]
        public UserRole Role { get; set; }

        // 🔹 Multi-tenant support
        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey("CompanyId")]
        public Company Company { get; set; } = null!;

        // 🔹 Leaves applied by user
        public ICollection<Leave> Leaves { get; set; } 
            = new List<Leave>();

        // 🔹 Refresh tokens (NEW)
        public ICollection<RefreshToken> RefreshTokens { get; set; } 
            = new List<RefreshToken>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int TotalLeaveBalance { get; set; } = 20;  // Default annual leaves

        public int UsedLeave { get; set; } = 0;
        public string Department { get; set; } = "General";
    }
