<div align="center">

<h3>NRDToCSV</h3>
  
![image](https://user-images.githubusercontent.com/14204888/132671281-c7f68d43-cbfa-47da-87db-3092c60ec55c.png)

</div>

#
**NinjaTrader 8** AddOn to convert NRD (`*.nrd`) market replay files to CSV (`*.csv`)<br>
(based on [not yet documented][market-data] `MarketReplay.DumpMarketDepth` feature)

## Quick Start

1. Download the latest AddOn **Source Code (zip)** file from the repository [releases][releases]
2. Import AddOn into NinjaTrader 8 via `Tools` / `Import` / `NinjaScript AddOn...`
3. Open NRD to CSV tool via `Tools` / `NRD to CSV`
5. Press **Convert** button to convert all `*.nrd` replay files (could take some time to proceed)
6. Check `Documents` \ `NinjaTrader 8` \ `db` \ `replay.csv` folder with the results

## Filter files to convert

You can specify filenames to convert with semicolon-separated regular expressions:

- Convert only gold commodity futures: `GC`
- Convert several instruments: `GC; HG; 6E`
- Convert files with names related only to December 2021: `202112\d{2}`<br>
  Means: `"2021"` `"12"` `<any 2 digits of Day>`
- Convert files with names related to all Decembers of Gold Commodity futures: `GC.*\d{4}12\d{2}`<br>
  Means: `"GC"` `<any chars>` `<any 4 digits of Year>` `"12"` `<any 2 digits of Day>`

## Converted `*.csv` file format

### Content example
```csv
L1;0;20210120050050;2300000;1855.8;2
L1;1;20210120050107;2140000;1855.4;8
L2;0;20210120050000;70000;0;0;;1855.5;1
```

### L1 Records
- `NinjaTrader.Data.MarketDataType`
```csharp
Ask = 0
Bid = 1
Last = 2
DailyHigh = 3
DailyLow = 4
DailyVolume = 5
LastClose = 6
Opening = 7
OpenInterest = 8
Settlement = 9
Unknown = 10
```
- `Timestamp` in `YYYYMMDDhhmmss` format (local NinjaTrader timezone is used)
- `Timestamp offset` as an integer amount of 100-nanoseconds (`1e-7`)
- `Price` value (local NinjaTrader price format is used for thousand/decimal separators)
- `Volume` value

### L2 Records
- `NinjaTrader.Data.MarketDataType`
```csharp
Ask = 0
Bid = 1
Last = 2
DailyHigh = 3
DailyLow = 4
DailyVolume = 5
LastClose = 6
Opening = 7
OpenInterest = 8
Settlement = 9
Unknown = 10
```
- `Timestamp` in `YYYYMMDDhhmmss` format (local NinjaTrader timezone is used)
- `Timestamp offset` as an integer amount of 100-nanoseconds (`1e-7`)
- `NinjaTrader.Cbi.Operation`
```csharp
Add = 0
Update = 1
Remove = 2
```
- `Position` in Order Book
- `MarketMaker` identifier
- `Price` value (local NinjaTrader price format is used for thousand/decimal separators)
- `Volume` value

## Change Log
This project adheres to [Semantic Versioning][semver].<br>
Every release, along with the migration instructions, is documented on the GitHub [Releases][releases] page.

## License
The code is available under the [MIT license][license].

## Contacts
Feel free to contact me at **@gmail.com**: **eugene.ilyin**

[market-data]: https://ninjatrader.com/support/forum/forum/ninjatrader-8/platform-technical-support-aa/1067384-more-info-on-marketreplay-dumpmarketdata-marketreplay-dumpmarketdepth
[releases]: https://github.com/eugeneilyin/nrdtocsv/releases
[license]: /License.txt
[semver]: http://semver.org
