export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data: T | null;
}

export interface PagedResult<T> {
  items: T[];
  nextCursor?: string;
}

export interface FieldError {
  field: string;
  message: string;
}

export interface ApiErrorResponse {
  success: false;
  message: string;
  errorCode: string;
  traceId: string;
  details?: FieldError[] | null;
}
