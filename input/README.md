# CRIS Data Input Directory

Place your CRIS CSV export files in this directory:

- crash.csv: Main crash records
- person.csv: Person information
- unit.csv: Vehicle/unit information
- damages.csv: Damage assessments (optional)
- charges.csv: Legal charges (optional)
- lookup.csv: Reference data (optional)

The processor will read crash.csv, person.csv, and unit.csv to generate the processed GeoJSON files for the web application.

Ensure the CSV files contain the following key fields:

## crash.csv
- CrashId
- CrashDate, CrashTime
- Latitude, Longitude
- CrashSeverity
- WeatherCondition, LightCondition, SurfaceCondition

## person.csv
- CrashId, PersonId
- InjurySeverity
- Age, Gender

## unit.csv
- CrashId, UnitId
- VehicleType
- TravelDirection
