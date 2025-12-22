//using Microsoft.EntityFrameworkCore;
//using System;
//using System.Collections.Generic;
//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;

//namespace AppointMe.Domain.DomainModels
//{
//    public class StaffMember
//    {
//        [Key]
//        public Guid Id { get; set; }

//        [Required]
//        [ForeignKey(nameof(Business))]
//        public Guid BusinessId { get; set; }

//        [Required, MaxLength(100)]
//        public string FirstName { get; set; } = default!;

//        [Required, MaxLength(100)]
//        public string LastName { get; set; } = default!;

//        [EmailAddress]
//        public string? Email { get; set; }

//        [Phone]
//        public string? Phone { get; set; }

//        public Guid? UserId { get; set; } 

       
//        public Business Business { get; set; } = default!;
//        public ICollection<StaffService> StaffServices { get; set; } = new List<StaffService>();

//        [Timestamp]
//        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
//    }

//    [PrimaryKey(nameof(StaffMemberId), nameof(ServiceOfferingId))] 
//    public class StaffService
//    {
//        public Guid StaffMemberId { get; set; }

//        public Guid ServiceOfferingId { get; set; }

//        [ForeignKey(nameof(StaffMemberId))]
//        public StaffMember StaffMember { get; set; } = default!;

//        [ForeignKey(nameof(ServiceOfferingId))]
//        public ServiceOffering ServiceOffering { get; set; } = default!;
//    }
//}
