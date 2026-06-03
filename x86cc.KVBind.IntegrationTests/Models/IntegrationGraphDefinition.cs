using x86cc.KVBind.Core;

namespace x86cc.KVBind.IntegrationTests.Models;

public static class IntegrationGraphDefinition
{
    public static KVNodeDefinition Create()
    {
        var builder = new KVBindBuilder<IntegrationGraph>();

        builder.Field(x => x.Text);
        builder.Field(x => x.DateLookingText);
        builder.Field(x => x.Flag);
        builder.Field(x => x.Count);
        builder.Field(x => x.LongCount);
        builder.Field(x => x.Ratio);
        builder.Field(x => x.Price);
        builder.Field(x => x.ExternalId);
        builder.Field(x => x.DateOnlyValue);
        builder.Field(x => x.DateTimeValue);
        builder.Field(x => x.DateTimeOffsetValue);
        builder.Field(x => x.TimeOnlyValue);
        builder.Field(x => x.Duration);
        builder.Field(x => x.OptionalNumber);
        builder.Field(x => x.Tags);
        builder.Field(x => x.Metrics);
        builder.Field(x => x.Details);
        builder.Field(x => x.SmartStatus, field => field.AllowedValues(IntegrationSmartStatus.All, value => value!.Id, value => value!.Label));
        builder.Field(x => x.CompensationType, field =>
        {
            field.AllowedValue(IntegrationCompensationType.Manager, "manager_flat", "Manager (Flat Fee)");
            field.AllowedValueComponent(IntegrationCompensationType.Assistant, "assistant_hourly", "Assistant (Hourly)", component => component
                .Template("Assistant {Hours}h @ {Rate}")
                .Placeholder<int>("Hours")
                .Placeholder<decimal>("Rate"));
        });

        builder.FieldGroup(x => x.Profile, profile =>
        {
            profile.Field(x => x.DisplayName);
            profile.FieldGroup(x => x.Address, address =>
            {
                address.Field(x => x.Line1);
                address.Field(x => x.City);
            });
        });

        builder.Collection(x => x.Orders, orders =>
        {
            orders.Item<IntegrationOrder>(order =>
            {
                order.Field(x => x.OrderNumber);
                order.Collection(x => x.Lines, lines =>
                {
                    lines.Item<IntegrationOrderLine>(line =>
                    {
                        line.Field(x => x.Sku);
                        line.Field(x => x.Quantity);
                        line.Collection(x => x.Adjustments, adjustments =>
                        {
                            adjustments.Item<IntegrationAdjustment>(adjustment =>
                            {
                                adjustment.Field(x => x.Reason);
                                adjustment.Field(x => x.Amount);
                                adjustment.Field(x => x.StatusHistory, field => field.AllowedElementValues(IntegrationSmartStatus.All, value => value.Id, value => value.Label));
                                adjustment.Field(x => x.StatusList, field => field.AllowedElementValues(IntegrationSmartStatus.All, value => value.Id, value => value.Label));
                                adjustment.Field(x => x.CompensationHistory, field =>
                                {
                                    field.AllowedElementValue(IntegrationCompensationType.Manager, "manager_flat", "Manager (Flat Fee)");
                                    field.AllowedElementValueComponent(IntegrationCompensationType.Assistant, "assistant_hourly", "Assistant (Hourly)", component => component
                                        .Template("Assistant {Hours}h @ {Rate}")
                                        .Placeholder<int>("Hours")
                                        .Placeholder<decimal>("Rate"));
                                });
                                adjustment.Field(x => x.CompensationList, field =>
                                {
                                    field.AllowedElementValue(IntegrationCompensationType.Manager, "manager_flat", "Manager (Flat Fee)");
                                    field.AllowedElementValueComponent(IntegrationCompensationType.Assistant, "assistant_hourly", "Assistant (Hourly)", component => component
                                        .Template("Assistant {Hours}h @ {Rate}")
                                        .Placeholder<int>("Hours")
                                        .Placeholder<decimal>("Rate"));
                                });
                            });
                        });
                    });
                });
            });
        });

        builder.NestedNode(x => x.Contact, contact =>
        {
            contact.Bind<PersonIntegrationContact>("PERSON", person =>
            {
                person.Field(x => x.FullName);
                person.Field(x => x.StatusHistory, field => field.AllowedElementValues(IntegrationSmartStatus.All, value => value.Id, value => value.Label));
                person.Field(x => x.StatusList, field => field.AllowedElementValues(IntegrationSmartStatus.All, value => value.Id, value => value.Label));
                person.Field(x => x.CompensationHistory, field =>
                {
                    field.AllowedElementValue(IntegrationCompensationType.Manager, "manager_flat", "Manager (Flat Fee)");
                    field.AllowedElementValueComponent(IntegrationCompensationType.Assistant, "assistant_hourly", "Assistant (Hourly)", component => component
                        .Template("Assistant {Hours}h @ {Rate}")
                        .Placeholder<int>("Hours")
                        .Placeholder<decimal>("Rate"));
                });
                person.Field(x => x.CompensationList, field =>
                {
                    field.AllowedElementValue(IntegrationCompensationType.Manager, "manager_flat", "Manager (Flat Fee)");
                    field.AllowedElementValueComponent(IntegrationCompensationType.Assistant, "assistant_hourly", "Assistant (Hourly)", component => component
                        .Template("Assistant {Hours}h @ {Rate}")
                        .Placeholder<int>("Hours")
                        .Placeholder<decimal>("Rate"));
                });
            });
            contact.Bind<CompanyIntegrationContact>("COMPANY", company => company.Field(x => x.CompanyName));
        });

        return builder.Build();
    }
}
