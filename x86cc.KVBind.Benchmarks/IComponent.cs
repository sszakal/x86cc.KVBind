namespace x86cc.KVBind.Benchmarks;

public interface IComponent
{
    public bool BooleanField { get; set; }
    
    public char CharField { get; set; }
    
    public int IntField { get; set; }
    
    public float FloatField { get; set; }

    public double DoubleField { get; set; }
    
    public decimal DecimalField { get; set; }
    
    public string StringField { get; set; } 
    
    public DateTime DateTimeField { get; set; }
    
    public DateTimeOffset DateTimeOffsetField { get; set; }
    
    public TimeOnly TimeOnlyField { get; set; }
    
    public DateOnly DateOnlyField { get; set; }
    
    public TimeSpan TimespanField { get; set; }    
    
    public Guid GuidField { get; set; }
    
    public int[] ArrayOfInts { get; set; }
    
    public string[] ArrayOfStrings { get; set; }
    
    public DateTime[] ArrayOfDates { get; set; }
}

public class NativeComponent : IComponent
{
    public bool BooleanField { get; set; }
    public char CharField { get; set; }
    public int IntField { get; set; }
    public float FloatField { get; set; }
    public double DoubleField { get; set; }
    public decimal DecimalField { get; set; }
    public string StringField { get; set; }
    public DateTime DateTimeField { get; set; }
    public DateTimeOffset DateTimeOffsetField { get; set; }
    public TimeOnly TimeOnlyField { get; set; }
    public DateOnly DateOnlyField { get; set; }
    public TimeSpan TimespanField { get; set; }
    public Guid GuidField { get; set; }
    public int[] ArrayOfInts { get; set; }
    public string[] ArrayOfStrings { get; set; }
    public DateTime[] ArrayOfDates { get; set; }
}

public class NativeComponentCollectionItemLevel1 : IComponent
{
    public bool BooleanField { get; set; }
    public char CharField { get; set; }
    public int IntField { get; set; }
    public float FloatField { get; set; }
    public double DoubleField { get; set; }
    public decimal DecimalField { get; set; }
    public string StringField { get; set; }
    public DateTime DateTimeField { get; set; }
    public DateTimeOffset DateTimeOffsetField { get; set; }
    public TimeOnly TimeOnlyField { get; set; }
    public DateOnly DateOnlyField { get; set; }
    public TimeSpan TimespanField { get; set; }
    public Guid GuidField { get; set; }
    public int[] ArrayOfInts { get; set; }
    public string[] ArrayOfStrings { get; set; }
    public DateTime[] ArrayOfDates { get; set; }
    
    public List<NativeComponentCollectionItemLevel2> Collection = new();
    
    public IEnumerable<IComponent> GetAllComponents()
    {
        return  Collection.SelectMany(x => x.GetAllComponents()).Union([this]);
    }
}

public class NativeComponentCollectionItemLevel2 : IComponent
{
    public bool BooleanField { get; set; }
    public char CharField { get; set; }
    public int IntField { get; set; }
    public float FloatField { get; set; }
    public double DoubleField { get; set; }
    public decimal DecimalField { get; set; }
    public string StringField { get; set; }
    public DateTime DateTimeField { get; set; }
    public DateTimeOffset DateTimeOffsetField { get; set; }
    public TimeOnly TimeOnlyField { get; set; }
    public DateOnly DateOnlyField { get; set; }
    public TimeSpan TimespanField { get; set; }
    public Guid GuidField { get; set; }
    public int[] ArrayOfInts { get; set; }
    public string[] ArrayOfStrings { get; set; }
    public DateTime[] ArrayOfDates { get; set; }
    
    public List<NativeComponentCollectionItemLevel3> Collection = new();
    
    public IEnumerable<IComponent> GetAllComponents()
    {
        return Collection;
    }
}

public class NativeComponentCollectionItemLevel3 : IComponent
{
    public bool BooleanField { get; set; }
    public char CharField { get; set; }
    public int IntField { get; set; }
    public float FloatField { get; set; }
    public double DoubleField { get; set; }
    public decimal DecimalField { get; set; }
    public string StringField { get; set; }
    public DateTime DateTimeField { get; set; }
    public DateTimeOffset DateTimeOffsetField { get; set; }
    public TimeOnly TimeOnlyField { get; set; }
    public DateOnly DateOnlyField { get; set; }
    public TimeSpan TimespanField { get; set; }
    public Guid GuidField { get; set; }
    public int[] ArrayOfInts { get; set; }
    public string[] ArrayOfStrings { get; set; }
    public DateTime[] ArrayOfDates { get; set; }
    
}

public class NativeRoot : NativeComponent
{
    public NativeComponent Component { get; set; } = new();

    public List<NativeComponentCollectionItemLevel1> Collection = new();

    public IEnumerable<IComponent> GetAllComponents()
    {
        return  Collection.SelectMany(x => x.GetAllComponents()).Union([Component]);
    }
}



