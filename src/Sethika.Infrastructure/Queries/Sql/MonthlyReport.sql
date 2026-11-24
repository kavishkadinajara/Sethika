/*  The monthly report (plan 09, M16): one read, in result sets, for ReportQueries.

    @first and @last are the month's Colombo days; @from and @to the same month as UTC instants, @to
    being the next month's first moment. Bookings belong to the day of their meal, gifts to the day
    they were given (DonatedAt), so a month's report holds what happened in it, whenever it was typed in.

    Served days are not here: they come from AvailabilityCalendar, the booking rules' own answer.   */

-- 1. Who the report is for.
SELECT i.Slug AS Slug, i.Name AS Name, i.NameSi AS NameSi, i.NameTa AS NameTa
FROM org.Institutions AS i
WHERE i.Id = @institutionId;

-- 2. Bookings of the month's meals, by status.
SELECT b.Status AS Status, COUNT(*) AS [Count]
FROM booking.Bookings AS b
JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
WHERE b.InstitutionId = @institutionId AND si.SlotDate >= @first AND si.SlotDate <= @last
GROUP BY b.Status;

-- 3. The slots to report on: every active one, and one since retired if it was booked this month.
SELECT s.PublicId AS MealSlot, s.Name AS Name, s.NameSi AS NameSi, s.NameTa AS NameTa, s.CreatedAt AS CreatedAt
FROM booking.MealSlots AS s
WHERE s.InstitutionId = @institutionId
  AND (s.IsActive = 1
       OR EXISTS (SELECT 1
                  FROM booking.SlotInstances AS si
                  JOIN booking.Bookings AS b ON b.SlotInstanceId = si.Id AND b.InstitutionId = si.InstitutionId
                  WHERE si.MealSlotId = s.Id AND si.InstitutionId = s.InstitutionId
                    AND si.SlotDate >= @first AND si.SlotDate <= @last
                    AND b.Status IN ('Approved', 'Completed')))
ORDER BY s.SortOrder, s.StartTime, s.Name;

-- 4. The days each slot had at least one approved or completed booking.
SELECT DISTINCT s.PublicId AS MealSlot, si.SlotDate AS SlotDate
FROM booking.Bookings AS b
JOIN booking.SlotInstances AS si ON si.Id = b.SlotInstanceId AND si.InstitutionId = b.InstitutionId
JOIN booking.MealSlots AS s ON s.Id = si.MealSlotId AND s.InstitutionId = si.InstitutionId
WHERE b.InstitutionId = @institutionId AND si.SlotDate >= @first AND si.SlotDate <= @last
  AND b.Status IN ('Approved', 'Completed');

-- 5. Money received, by method. A voided gift is out of every total (M6).
SELECT m.Code AS Code, m.Name AS Name, m.NameSi AS NameSi, m.NameTa AS NameTa,
       COUNT(*) AS Gifts, SUM(d.Amount) AS Amount
FROM giving.Donations AS d
JOIN core.LookupValues AS m ON m.Id = d.PaymentMethodLookupId
WHERE d.InstitutionId = @institutionId AND d.DonatedAt >= @from AND d.DonatedAt < @to
  AND d.Kind = 'Money' AND d.IsVoided = 0
GROUP BY m.Code, m.Name, m.NameSi, m.NameTa, m.SortOrder
ORDER BY m.SortOrder, m.Code;

-- 6. Gifts of items, by the category of the need they came for. A gift for no need has no category,
--    and quantities are not summed: books and kilograms do not add up.
SELECT c.Code AS Code, c.Name AS Name, c.NameSi AS NameSi, c.NameTa AS NameTa,
       COUNT(*) AS Gifts, SUM(d.EstimatedValue) AS EstimatedValue
FROM giving.Donations AS d
LEFT JOIN giving.Needs AS n ON n.Id = d.NeedId AND n.InstitutionId = d.InstitutionId
LEFT JOIN core.LookupValues AS c ON c.Id = n.CategoryLookupId
WHERE d.InstitutionId = @institutionId AND d.DonatedAt >= @from AND d.DonatedAt < @to
  AND d.Kind = 'InKind' AND d.IsVoided = 0
GROUP BY c.Code, c.Name, c.NameSi, c.NameTa, c.SortOrder
ORDER BY CASE WHEN c.Code IS NULL THEN 1 ELSE 0 END, c.SortOrder, c.Code;

-- 7. The month's voided gifts, listed apart.
SELECT d.PublicId AS PublicId, d.ReceiptNo AS ReceiptNo, d.DonatedAt AS DonatedAt, d.DonorName AS DonorName,
       d.Kind AS Kind, d.Amount AS Amount, d.ItemDescription AS Items, d.Quantity AS Quantity,
       d.VoidReason AS VoidReason, d.VoidedAt AS VoidedAt, v.FullName AS VoidedByName
FROM giving.Donations AS d
JOIN auth.Users AS v ON v.Id = d.VoidedBy
WHERE d.InstitutionId = @institutionId AND d.DonatedAt >= @from AND d.DonatedAt < @to AND d.IsVoided = 1
ORDER BY d.DonatedAt, d.Id;

-- 8. Needs: put on the board this month, fulfilled this month, and still open as the month ended
--    (as of now, for a month not over). A void that reopened a need cleared EndedAt (M30, M37).
SELECT COALESCE(SUM(CASE WHEN n.OpenedAt >= @from AND n.OpenedAt < @to THEN 1 ELSE 0 END), 0) AS Opened,
       COALESCE(SUM(CASE WHEN n.Status = 'Fulfilled' AND n.EndedAt >= @from AND n.EndedAt < @to THEN 1 ELSE 0 END), 0) AS Fulfilled,
       COALESCE(SUM(CASE WHEN n.OpenedAt < @to AND (n.EndedAt IS NULL OR n.EndedAt >= @to) THEN 1 ELSE 0 END), 0) AS StillOpen
FROM giving.Needs AS n
WHERE n.InstitutionId = @institutionId;
