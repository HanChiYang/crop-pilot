export * from './auth.service';
import { AuthService } from './auth.service';
export * from './fields.service';
import { FieldsService } from './fields.service';
export * from './overview.service';
import { OverviewService } from './overview.service';
export * from './plantings.service';
import { PlantingsService } from './plantings.service';
export const APIS = [AuthService, FieldsService, OverviewService, PlantingsService];
