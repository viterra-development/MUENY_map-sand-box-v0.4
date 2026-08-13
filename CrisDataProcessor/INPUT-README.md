# CRIS Data Input Directory

Place your CRIS CSV export files in this directory:

- crash.csv: Main crash records
- primaryperson.csv: Primary person information (drivers and key persons)
- unit.csv: Vehicle/unit information
- damages.csv: Damage assessments (optional)
- charges.csv: Legal charges (optional)
- lookup.csv: Reference data (optional)

The processor will read crash.csv, primaryperson.csv, and unit.csv to generate the processed GeoJSON files for the web application.

Ensure the CSV files contain the following key fields:

## crash.csv
- CrashId
- CrashDate, CrashTime
- Latitude, Longitude
- CrashSeverity
- WeatherCondition, LightCondition, SurfaceCondition

## primaryperson.csv
- CrashId, PersonId
- InjurySeverity
- Age, Gender

## unit.csv
- CrashId, UnitId
- VehicleType
- TravelDirection
