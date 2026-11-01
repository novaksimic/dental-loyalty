using System;
using System.Collections.Generic;
using System.Text;
using DentalLoyalty.Domain.Exceptions;

namespace DentalLoyalty.Domain.Entities
{
    public class Patient
    {
        public Guid Id { get; private set; }

        public Guid ClinicId { get; private set; }

        public Guid? UserId { get; private set; }

        public string FirstName { get; private set; }

        public string LastName { get; private set; }

        public string PhoneNumber { get; private set; }

        public string? Email { get; private set; }

        public bool IsActive { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        private Patient()
        {
            FirstName = null!;
            LastName = null!;
            PhoneNumber = null!;
        }

        public Patient(
            Guid clinicId,
            string firstName,
            string lastName,
            string phoneNumber,
            string? email = null)
        {
            Id = Guid.NewGuid();
            ClinicId = clinicId;
            FirstName = firstName;
            LastName = lastName;
            PhoneNumber = phoneNumber;
            Email = email;
            IsActive = true;
            CreatedAt = DateTime.UtcNow;
        }

        public void UpdateContactInformation(
            string phoneNumber,
            string? email)
        {
            PhoneNumber = phoneNumber;
            Email = email;
            UpdatedAt = DateTime.UtcNow;
        }

        // A Patient is linked to at most one User, and the link is never handed
        // over to another User. Linking the same User again changes nothing.
        public void LinkUser(Guid userId)
        {
            if (UserId == userId)
            {
                return;
            }

            if (UserId is not null)
            {
                throw new DomainException(
                    "The patient is already linked to another user account.");
            }

            UserId = userId;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Deactivate()
        {
            IsActive = false;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Activate()
        {
            IsActive = true;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
