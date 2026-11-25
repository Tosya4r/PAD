namespace PAD.Domain;

public enum ProjectStatus { Planned, WaitingStart, InProgress, OnHold, InReview, Delivered, Completed, Cancelled }

public enum Priority { Low, Medium, High, Critical }

public enum WorkStatus { Todo, InProgress, Waiting, Done }

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Цвет категории (#RRGGBB) для диаграмм. Назначается автоматически из палитры.</summary>
    public string Color { get; set; } = "#2F5BEA";
}

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string ClientName { get; set; } = "";
    public string? ClientContact { get; set; }
    public string Goal { get; set; } = "";
    public Priority Priority { get; set; } = Priority.Medium;
    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;

    // Коммерция. Price — договорная стоимость, НЕ заработок.
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? Deadline { get; set; }
    public string PaymentTerms { get; set; } = "";
    public decimal? PrepaymentAmount { get; set; }
    public string FinanceNotes { get; set; } = "";

    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<Payment> Payments { get; set; } = new();
    public List<ProjectDelivery> Deliveries { get; set; } = new();
    public List<ProjectPriceHistory> PriceHistory { get; set; } = new();
    public List<ProjectStatusHistory> StatusHistory { get; set; } = new();
}

/// <summary>Платёж. Заработком считается только при IsReceived = true.</summary>
public class Payment
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public decimal Amount { get; set; }          // всегда положительное
    public string Currency { get; set; } = "EUR";
    public DateOnly ReceivedDate { get; set; }
    public PaymentType Type { get; set; }
    public string Purpose { get; set; } = "";
    public string Comment { get; set; } = "";
    public bool IsReceived { get; set; } = true;
    public decimal? ManualRate { get; set; }     // ручной курс, только если задан пользователем
    public string? RateCurrency { get; set; }

    public PaymentRecord ToRecord() => new(Id, Amount, Currency, ReceivedDate, Type, IsReceived);
}

/// <summary>Фиксация сдачи. Повторное открытие проекта не удаляет запись, а ставит ReopenedAt.</summary>
public class ProjectDelivery
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public DateTime DeliveredAt { get; set; }
    public decimal FinalPrice { get; set; }
    public string Currency { get; set; } = "EUR";
    public string ProductUrl { get; set; } = "";
    public string Comment { get; set; } = "";
    public string HandoverResult { get; set; } = "";
    public DateTime? ReopenedAt { get; set; }
}

public class ProjectPriceHistory
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public DateTime ChangedAt { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Reason { get; set; } = "";
}

public class ProjectStatusHistory
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public DateTime ChangedAt { get; set; }
    public ProjectStatus OldStatus { get; set; }
    public ProjectStatus NewStatus { get; set; }
}

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public WorkStatus Status { get; set; } = WorkStatus.Todo;
    public Priority Priority { get; set; } = Priority.Medium;
    public DateOnly? Due { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
}
