import { AuditableEntity } from '../../../shared/models/common.model';
import { RoomStatus } from '../../../shared/models/enums';

export interface Property extends AuditableEntity {
  landlordId: string;
  name: string;
  address: string;
  electricityRate: number; // C# long -> TS number
  waterRate: number; // C# long -> TS number
  serviceFee: number; // C# long -> TS number
  rooms?: Room[];
}

export interface RoomArea extends AuditableEntity {
  roomId: string;
  name: string;
  minPhotos: number;
  sortOrder: number;
}

export interface Room extends AuditableEntity {
  propertyId: string;
  landlordId: string;
  code: string;
  monthlyRent: number; // C# long -> TS number
  status: RoomStatus;
  version: number; // C# uint (xmin concurrency token) -> TS number
  property?: Property;
  areas?: RoomArea[];
}
