export interface JobDescription {
  Id: string;
  Title: string;
  Company: string;
  Description: string;
  Location?: string;
  Salary?: string;
  WorkdayId?: string;
  Timestamp: string;
}

export interface JobDescriptionSubmission {
  title: string;
  company: string;
  description: string;
  location?: string;
  salary?: string;
  workdayId?: string;
}