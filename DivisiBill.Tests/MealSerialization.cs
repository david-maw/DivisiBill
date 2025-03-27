namespace DivisiBill.Tests;
public class MealSerialization
{
    internal const string FakeBillXml = """
        <Meal>
          <CreationReason>ElapsedTime</CreationReason>
          <SaveReason>time</SaveReason>
          <SaverVersion>6.2.24</SaverVersion>
          <DataVersion>1.1</DataVersion>
          <Restaurant>Queasy Diner</Restaurant>
          <CreationTime>2025-03-26T10:43:05-07:00</CreationTime>
          <LastChangeTime>2025-03-26T10:43:06-07:00</LastChangeTime>
          <TipRate>0.2</TipRate>
          <TipOnTax>false</TipOnTax>
          <TaxOnDiscount>false</TaxOnDiscount>
          <TaxRate>0.0775</TaxRate>
          <ScannedSubTotal>125.00</ScannedSubTotal>
          <Costs>
            <PersonCost
              PersonGUID="ff6a1c63-3c0d-4f85-9d13-1f442ee718ee"
              Nickname="Bill"
              DinerIndex="1" />
            <PersonCost
              PersonGUID="45ed1c1c-d48f-4005-9b0d-e1768ad082ec"
              Nickname="Chris"
              DinerIndex="2" />
            <PersonCost
              PersonGUID="2ff7856b-2340-4c4c-98cb-096ccca85f10"
              Nickname="Craig"
              DinerIndex="3" />
          </Costs>
          <LineItems>
            <LineItem
              SharesList="111"
              ItemName="Appetizer"
              Amount="20" />
            <LineItem
              SharesList="111"
              ItemName="Discount"
              Amount="-10" />
            <LineItem
              SharesList="001"
              ItemName="Tasty Chicken"
              Amount="30" />
            <LineItem
              SharesList="1"
              ItemName="Overdone Beef"
              Comped="true"
              Amount="40" />
            <LineItem
              SharesList="21"
              ItemName="Wine"
              Amount="60" />
            <LineItem
              SharesList="01"
              ItemName="Fish &amp; Chips"
              Amount="20" />
            <LineItem
              ItemName="Mystery item"
              Amount="5" />
          </LineItems>
        </Meal>
        """; // End of FakeBillXml

}
