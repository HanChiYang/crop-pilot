import { PlantingRsDto } from './generated/model/plantingRsDto';
import { PlantingStatus } from './generated/model/plantingStatus';

export const plantingStatusLabel: Record<PlantingStatus, string> = {
  planned: '規劃中',
  growing: '栽培中',
  harvesting: '採收中',
  completed: '已結束',
};

export function plantingDate(planting: PlantingRsDto): string {
  if (planting.actualPlantingDate)
    return `實際栽種日 ${planting.actualPlantingDate}`;
  if (planting.plannedPlantingDate)
    return `預定栽種日 ${planting.plannedPlantingDate}`;
  return '尚未設定';
}
