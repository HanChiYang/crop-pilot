import { CompletionDateLowerBoundSource } from './generated/model/completionDateLowerBoundSource';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { PlantingStatus } from './generated/model/plantingStatus';

export const plantingStatusLabel: Record<PlantingStatus, string> = {
  planned: '規劃中',
  growing: '栽培中',
  harvesting: '採收中',
  completed: '已結束',
};

export const completionDateLowerBoundLabel: Record<CompletionDateLowerBoundSource, string> = {
  harvest: '最後一筆採收',
  materialApplication: '最後一筆資材施用',
  irrigation: '最後一筆灌溉',
  actualPlanting: '實際栽種日',
};

export function formatSlashDate(isoDate: string): string {
  return isoDate.replaceAll('-', '/');
}

export function plantingDate(planting: PlantingRsDto): string {
  if (planting.actualPlantingDate)
    return `實際栽種日 ${planting.actualPlantingDate}`;
  if (planting.plannedPlantingDate)
    return `預定栽種日 ${planting.plannedPlantingDate}`;
  return '尚未設定';
}
