using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OmniKiosk.Wpf.Models;

namespace OmniKiosk.Wpf.Services
{
    public class DataStorageService
    {
        private readonly string _dataDirectory;
        private readonly string _sendersFile;
        private readonly string _beneficiariesFile;

        public DataStorageService()
        {
            _dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OmniRemit", "Data");

            Directory.CreateDirectory(_dataDirectory);

            _sendersFile = Path.Combine(_dataDirectory, "senders.json");
            _beneficiariesFile = Path.Combine(_dataDirectory, "beneficiaries.json");
        }

        #region Sender Methods
        public SenderModel FindSenderByICAndMobile(string icType, string icNumber, string mobileNo)
        {
            var allSenders = GetAllSenders();

            return allSenders.FirstOrDefault(s =>
                s.ICType.Equals(icType, StringComparison.OrdinalIgnoreCase) &&
                s.ICNumber.Equals(icNumber, StringComparison.OrdinalIgnoreCase) &&
                s.MobileNo.Equals(mobileNo, StringComparison.OrdinalIgnoreCase));
        }

        public List<SenderModel> GetAllSenders()
        {
            try
            {
                if (!File.Exists(_sendersFile))
                    return new List<SenderModel>();

                var json = File.ReadAllText(_sendersFile);
                var senders = JsonSerializer.Deserialize<List<SenderModel>>(json) ?? new List<SenderModel>();
                return senders.OrderByDescending(s => s.LastUsedDate).ToList();
            }
            catch
            {
                return new List<SenderModel>();
            }
        }

        public void SaveSender(SenderModel sender)
        {
            try
            {
                var senders = GetAllSenders();

                var existing = senders.FirstOrDefault(s => s.ICNumber == sender.ICNumber);
                if (existing != null)
                {
                    // Update existing
                    existing.FullName = sender.FullName;
                    existing.ICType = sender.ICType;
                    existing.DateOfBirth = sender.DateOfBirth;
                    existing.Nationality = sender.Nationality;
                    existing.MobileNo = sender.MobileNo;
                    existing.Email = sender.Email;
                    existing.Address = sender.Address;
                    existing.City = sender.City;
                    existing.Postcode = sender.Postcode;
                    existing.State = sender.State;
                    existing.Country = sender.Country;
                    existing.Occupation = sender.Occupation;
                    existing.Employer = sender.Employer;
                    existing.SourceOfFunds = sender.SourceOfFunds;
                    existing.Photo = sender.Photo;
                    existing.LastUsedDate = DateTime.Now;
                }
                else
                {
                    // Add new
                    sender.Id = senders.Any() ? senders.Max(s => s.Id) + 1 : 1;
                    sender.CreatedDate = DateTime.Now;
                    sender.LastUsedDate = DateTime.Now;
                    senders.Add(sender);
                }

                var json = JsonSerializer.Serialize(senders, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_sendersFile, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save sender: {ex.Message}");
            }
        }

        public void DeleteSender(int senderId)
        {
            try
            {
                var senders = GetAllSenders();
                senders.RemoveAll(s => s.Id == senderId);
                var json = JsonSerializer.Serialize(senders, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_sendersFile, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete sender: {ex.Message}");
            }
        }

        public void UpdateSenderLastUsed(string icNumber)
        {
            try
            {
                var senders = GetAllSenders();
                var sender = senders.FirstOrDefault(s => s.ICNumber == icNumber);
                if (sender != null)
                {
                    sender.LastUsedDate = DateTime.Now;
                    var json = JsonSerializer.Serialize(senders, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_sendersFile, json);
                }
            }
            catch { }
        }
        #endregion

        #region Beneficiary Methods

        // Get all beneficiaries (private method)
        private List<BeneficiaryModel> GetAllBeneficiaries()
        {
            try
            {
                if (!File.Exists(_beneficiariesFile))
                    return new List<BeneficiaryModel>();

                var json = File.ReadAllText(_beneficiariesFile);
                var beneficiaries = JsonSerializer.Deserialize<List<BeneficiaryModel>>(json) ?? new List<BeneficiaryModel>();
                return beneficiaries;
            }
            catch
            {
                return new List<BeneficiaryModel>();
            }
        }

        // Get beneficiaries for specific customer and country
        public List<BeneficiaryModel> GetBeneficiariesByCustomerAndCountry(string customerIdNo, string countryCode)
        {
            try
            {
                if (string.IsNullOrEmpty(customerIdNo))
                    return new List<BeneficiaryModel>();

                var allBeneficiaries = GetAllBeneficiaries();

                // Filter by customer ID and country
                return allBeneficiaries
                    .Where(b => b.CustomerIdNo == customerIdNo && b.CountryCode == countryCode)
                    .OrderByDescending(b => b.LastUsedDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading beneficiaries: {ex.Message}");
                return new List<BeneficiaryModel>();
            }
        }

        // Legacy method for backward compatibility
        public List<BeneficiaryModel> GetBeneficiariesByCountry(string countryCode)
        {
            return GetAllBeneficiaries()
                .Where(b => b.CountryCode == countryCode)
                .OrderByDescending(b => b.LastUsedDate)
                .ToList();
        }

        public void SaveBeneficiary(BeneficiaryModel beneficiary)
        {
            try
            {
                var beneficiaries = GetAllBeneficiaries();

                // Check if beneficiary exists (same customer, same account, same bank, same country)
                var existing = beneficiaries.FirstOrDefault(b =>
                    b.CustomerIdNo == beneficiary.CustomerIdNo &&
                    b.CountryCode == beneficiary.CountryCode &&
                    b.AccountNo == beneficiary.AccountNo &&
                    b.BankCode == beneficiary.BankCode);

                if (existing != null)
                {
                    // Update existing
                    existing.FullName = beneficiary.FullName;
                    existing.FirstName = beneficiary.FirstName;
                    existing.LastName = beneficiary.LastName;
                    existing.Country = beneficiary.Country;
                    existing.MobileNo = beneficiary.MobileNo;
                    existing.Nationality = beneficiary.Nationality;
                    existing.Address = beneficiary.Address;
                    existing.City = beneficiary.City;
                    existing.BankName = beneficiary.BankName;
                    existing.Relationship = beneficiary.Relationship;
                    existing.LastUsedDate = DateTime.Now;
                    existing.UsageCount++;

                    // Update BNM fields
                    existing.IFSC = beneficiary.IFSC;
                    existing.RoutingNumber = beneficiary.RoutingNumber;
                    existing.SwiftCode = beneficiary.SwiftCode;
                    existing.IBAN = beneficiary.IBAN;
                }
                else
                {
                    // Add new
                    beneficiary.Id = beneficiaries.Any() ? beneficiaries.Max(b => b.Id) + 1 : 1;
                    beneficiary.CreatedDate = DateTime.Now;
                    beneficiary.LastUsedDate = DateTime.Now;
                    beneficiary.UsageCount = 1;
                    beneficiaries.Add(beneficiary);
                }

                var json = JsonSerializer.Serialize(beneficiaries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_beneficiariesFile, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save beneficiary: {ex.Message}");
            }
        }

        public void UpdateBeneficiaryLastUsed(int beneficiaryId)
        {
            try
            {
                var beneficiaries = GetAllBeneficiaries();
                var beneficiary = beneficiaries.FirstOrDefault(b => b.Id == beneficiaryId);

                if (beneficiary != null)
                {
                    beneficiary.LastUsedDate = DateTime.Now;
                    beneficiary.UsageCount++;
                    var json = JsonSerializer.Serialize(beneficiaries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_beneficiariesFile, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating beneficiary: {ex.Message}");
            }
        }

        public void DeleteBeneficiary(int beneficiaryId)
        {
            try
            {
                var beneficiaries = GetAllBeneficiaries();
                beneficiaries.RemoveAll(b => b.Id == beneficiaryId);
                var json = JsonSerializer.Serialize(beneficiaries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_beneficiariesFile, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete beneficiary: {ex.Message}");
            }
        }

        #endregion
    }
}